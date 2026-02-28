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
    | Commit of sessionId: string * message: string option
    | ShareNotes of remote: string
    | Init of provider: string option
    | Help

type CommandResult =
    | Completed
    | Failed of message: string
