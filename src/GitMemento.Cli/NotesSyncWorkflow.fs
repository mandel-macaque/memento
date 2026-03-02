namespace GitMemento

open System
open System.Threading.Tasks
open Serilog

type NotesSyncWorkflow(git: IGitService, output: IUserOutput) =
    static member private LocalNotesRefs =
        [ "refs/notes/commits"
          "refs/notes/memento-full-audit" ]

    static member private BackupRootRef() =
        $"refs/notes/memento-backups/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"

    static member private BackupRefFor(notesRef: string, backupRootRef: string) =
        let suffix = notesRef.Replace("refs/notes/", "")
        $"{backupRootRef}/{suffix}"

    static member private MergeFailureMessage(notesRef: string, backupRef: string, mergeError: string) =
        let recoveryHint =
            $"Notes merge failed for '{notesRef}'. Local backup is at '{backupRef}'. You can restore with: git update-ref {notesRef} $(git rev-parse {backupRef})"

        $"{mergeError}{Environment.NewLine}{recoveryHint}"

    member private _.CreateBackupIfPresentAsync
        (notesRef: string, backupRef: string, localObjectId: string option)
        : Task<Result<unit, string>> =
        task {
            match localObjectId with
            | Some objectId ->
                Log.Debug("Creating backup ref {BackupRef} from object {ObjectId}", backupRef, objectId)
                let! backupResult = git.UpdateRefAsync(backupRef, objectId)
                match backupResult with
                | Error err -> return Error err
                | Ok _ ->
                    output.Info($"Created notes backup at '{backupRef}'.")
                    return Ok()
            | None ->
                output.Info($"No local notes ref '{notesRef}' found yet; skipping backup creation.")
                return Ok()
        }

    member private _.ShareNotesAsync(remote: string, successMessage: string) : Task<CommandResult> =
        task {
            let! shareResult = git.ShareNotesAsync(remote)
            match shareResult with
            | Error shareError -> return CommandResult.Failed shareError
            | Ok _ ->
                output.Info(successMessage)
                return CommandResult.Completed
        }

    member private _.ReconcileRefAsync(
        remote: string,
        strategy: string,
        notesRef: string,
        backupRef: string,
        remoteNotesRef: string,
        localObjectId: string option,
        remoteObjectId: string option
    ) : Task<Result<bool, string>> =
        task {
            match localObjectId, remoteObjectId with
            | None, None ->
                output.Info($"No local or remote notes were found to sync for '{notesRef}'.")
                return Ok false
            | None, Some objectId ->
                Log.Debug("Initializing local notes ref {Ref} from remote notes object {ObjectId}", notesRef, objectId)
                let! initResult = git.UpdateRefAsync(notesRef, objectId)
                match initResult with
                | Error initError -> return Error initError
                | Ok _ -> return Ok true
            | Some _, None ->
                output.Info($"No notes found on remote '{remote}' to merge for '{notesRef}'; local notes unchanged.")
                return Ok false
            | Some _, Some _ ->
                Log.Debug("Merging notes ref {SourceRef} into {TargetRef} using strategy {Strategy}", remoteNotesRef, notesRef, strategy)
                let! mergeResult = git.MergeNotesAsync(notesRef, remoteNotesRef, strategy)
                match mergeResult with
                | Error mergeError -> return Error(NotesSyncWorkflow.MergeFailureMessage(notesRef, backupRef, mergeError))
                | Ok _ -> return Ok true
        }

    member this.ExecuteAsync(remote: string, strategy: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before syncing notes")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                let backupRootRef = NotesSyncWorkflow.BackupRootRef()
                let remoteNamespace = $"refs/notes/remote/{remote}"

                Log.Debug("Ensuring notes fetch config for remote {Remote}", remote)
                let! configResult = git.EnsureNotesFetchConfiguredAsync(remote)
                match configResult with
                | Error configError -> return CommandResult.Failed configError
                | Ok _ ->
                    // Fetch into a namespaced ref first so we can merge deliberately and keep control of conflicts.
                    Log.Debug("Fetching notes from remote {Remote} into namespace {Namespace}", remote, remoteNamespace)
                    let! fetchResult = git.FetchNotesToNamespaceAsync(remote, remoteNamespace)
                    match fetchResult with
                    | Error fetchError -> return CommandResult.Failed fetchError
                    | Ok _ ->
                        let mutable changed = false
                        let mutable failure: string option = None

                        for notesRef in NotesSyncWorkflow.LocalNotesRefs do
                            if failure.IsNone then
                                let remoteSuffix = notesRef.Replace("refs/notes/", "")
                                let remoteNotesRef = $"{remoteNamespace}/{remoteSuffix}"
                                let scopedBackupRef = NotesSyncWorkflow.BackupRefFor(notesRef, backupRootRef)

                                Log.Debug("Checking local notes ref {Ref}", notesRef)
                                let! localRefResult = git.GetRefObjectIdAsync(notesRef)
                                match localRefResult with
                                | Error readError -> failure <- Some readError
                                | Ok localObjectId ->
                                    let! backupResult = this.CreateBackupIfPresentAsync(notesRef, scopedBackupRef, localObjectId)
                                    match backupResult with
                                    | Error backupError -> failure <- Some backupError
                                    | Ok _ ->
                                        let! remoteRefResult = git.GetRefObjectIdAsync(remoteNotesRef)
                                        match remoteRefResult with
                                        | Error readError -> failure <- Some readError
                                        | Ok remoteObjectId ->
                                            let! reconcileResult =
                                                this.ReconcileRefAsync(
                                                    remote,
                                                    strategy,
                                                    notesRef,
                                                    scopedBackupRef,
                                                    remoteNotesRef,
                                                    localObjectId,
                                                    remoteObjectId
                                                )

                                            match reconcileResult with
                                            | Error err -> failure <- Some err
                                            | Ok didChange -> changed <- changed || didChange

                        match failure with
                        | Some err -> return CommandResult.Failed err
                        | None when not changed ->
                            output.Info("No local or remote notes were found to sync.")
                            return CommandResult.Completed
                        | None ->
                            return!
                                this.ShareNotesAsync(
                                    remote,
                                    $"Synced git notes with remote '{remote}' using strategy '{strategy}'."
                                )
        }
