namespace GitMemento

open System

type MessageRole =
    | User
    | Assistant
    | System
    | Tool

type SessionMessage =
    { Role: MessageRole
      Content: string
      Timestamp: DateTimeOffset option }

type SessionData =
    { Id: string
      Provider: string
      Title: string option
      Messages: SessionMessage list }

type SessionRef =
    { Id: string
      Title: string option }

type Command =
    | Commit of sessionId: string * messages: string list
    | Amend of sessionId: string option * messages: string list
    | ShareNotes of remote: string
    | Push of remote: string
    | NotesSync of remote: string * strategy: string
    | NotesRewriteSetup
    | NotesCarry of onto: string * fromRange: string
    | Init of provider: string option
    | Version
    | Help

type CommandResult =
    | Completed
    | Failed of message: string
