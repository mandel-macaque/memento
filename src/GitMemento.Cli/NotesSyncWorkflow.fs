namespace GitMemento

open System
open System.Threading.Tasks
open Serilog

type NotesSyncWorkflow(git: IGitService, output: IUserOutput) =
    static member private LocalNotesRef = "refs/notes/commits"

    static member private BackupRef() =
        $"refs/notes/memento-backups/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"

    static member private MergeFailureMessage(backupRef: string, mergeError: string) =
        let recoveryHint =
            $"Notes merge failed. Local backup is at '{backupRef}'. You can restore with: git update-ref refs/notes/commits $(git rev-parse {backupRef})"

        $"{mergeError}{Environment.NewLine}{recoveryHint}"

    member private _.CreateBackupIfPresentAsync(backupRef: string, localObjectId: string option) : Task<Result<unit, string>> =
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
                output.Info("No local notes ref found yet; skipping backup creation.")
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

    member private this.ReconcileAsync(
        remote: string,
        strategy: string,
        backupRef: string,
        remoteNotesRef: string,
        localObjectId: string option,
        remoteObjectId: string option
    ) : Task<CommandResult> =
        task {
            match localObjectId, remoteObjectId with
            | None, None ->
                output.Info("No local or remote notes were found to sync.")
                return CommandResult.Completed
            | None, Some objectId ->
                Log.Debug("Initializing local notes ref from remote notes object {ObjectId}", objectId)
                let! initResult = git.UpdateRefAsync(NotesSyncWorkflow.LocalNotesRef, objectId)
                match initResult with
                | Error initError -> return CommandResult.Failed initError
                | Ok _ ->
                    return!
                        this.ShareNotesAsync(
                            remote,
                            $"Initialized local notes from '{remote}' and shared them back."
                        )
            | Some _, None ->
                output.Info($"No notes found on remote '{remote}' to merge; local notes unchanged.")
                return CommandResult.Completed
            | Some _, Some _ ->
                Log.Debug("Merging notes ref {SourceRef} using strategy {Strategy}", remoteNotesRef, strategy)
                let! mergeResult = git.MergeNotesAsync(remoteNotesRef, strategy)
                match mergeResult with
                | Error mergeError -> return CommandResult.Failed(NotesSyncWorkflow.MergeFailureMessage(backupRef, mergeError))
                | Ok _ ->
                    return!
                        this.ShareNotesAsync(
                            remote,
                            $"Synced git notes with remote '{remote}' using strategy '{strategy}'."
                        )
        }

    member this.ExecuteAsync(remote: string, strategy: string) : Task<CommandResult> =
        task {
            Log.Debug("Validating git repository before syncing notes")
            let! repoCheck = git.EnsureInRepositoryAsync()
            match repoCheck with
            | Error message -> return CommandResult.Failed message
            | Ok _ ->
                let backupRef = NotesSyncWorkflow.BackupRef()
                let remoteNamespace = $"refs/notes/remote/{remote}"
                let remoteNotesRef = $"{remoteNamespace}/commits"

                Log.Debug("Ensuring notes fetch config for remote {Remote}", remote)
                let! configResult = git.EnsureNotesFetchConfiguredAsync(remote)
                match configResult with
                | Error configError -> return CommandResult.Failed configError
                | Ok _ ->
                    Log.Debug("Checking local notes ref {Ref}", NotesSyncWorkflow.LocalNotesRef)
                    let! localRefResult = git.GetRefObjectIdAsync(NotesSyncWorkflow.LocalNotesRef)
                    match localRefResult with
                    | Error readError -> return CommandResult.Failed readError
                    | Ok localObjectId ->
                        let! backupResult = this.CreateBackupIfPresentAsync(backupRef, localObjectId)
                        match backupResult with
                        | Error backupError -> return CommandResult.Failed backupError
                        | Ok _ ->
                            // Fetch into a namespaced ref first so we can merge deliberately and keep control of conflicts.
                            Log.Debug("Fetching notes from remote {Remote} into namespace {Namespace}", remote, remoteNamespace)
                            let! fetchResult = git.FetchNotesToNamespaceAsync(remote, remoteNamespace)
                            match fetchResult with
                            | Error fetchError -> return CommandResult.Failed fetchError
                            | Ok _ ->
                                let! remoteRefResult = git.GetRefObjectIdAsync(remoteNotesRef)
                                match remoteRefResult with
                                | Error readError -> return CommandResult.Failed readError
                                | Ok remoteObjectId ->
                                    return!
                                        this.ReconcileAsync(
                                            remote,
                                            strategy,
                                            backupRef,
                                            remoteNotesRef,
                                            localObjectId,
                                            remoteObjectId
                                        )
        }
