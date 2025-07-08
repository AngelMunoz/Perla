module Pages.Notes

open Browser.Types

open Elmish
open Lit
open Lit.Elmish

type private Note =
  { Id: int
    Title: string
    Body: string }

type private State =
  { CurrentNote: Note option
    Notes: Note list }

type private Msg =
  | Save
  | Remove of Note
  | SetTitle of string
  | SetBody of string

let private init () =
  { CurrentNote = None
    Notes = List.empty },
  Cmd.none

let private update msg state =
  match msg with
  | Save ->
    match state.CurrentNote
          |> Option.map (fun note ->
            { note with Id = (state.Notes |> Seq.length) + 1 })
      with
    | Some note ->
      { state with
          Notes =
            { note with Id = state.Notes.Length + 1 }
            :: state.Notes },
      Cmd.none
    | None -> state, Cmd.none

  | Remove note ->
    { state with Notes = state.Notes |> List.filter (fun n -> n <> note) },
    Cmd.none
  | SetTitle title ->
    let current =
      state.CurrentNote
      |> Option.map (fun current -> { current with Title = title })
      |> Option.orElse (Some { Id = 0; Title = ""; Body = "" })

    { state with CurrentNote = current }, Cmd.none
  | SetBody body ->
    let current =
      state.CurrentNote
      |> Option.map (fun current -> { current with Body = body })
      |> Option.orElse (Some { Id = 0; Title = ""; Body = "" })

    { state with CurrentNote = current }, Cmd.none

let private noteTemplate (note: Note) (index: int) =
  html $"<li>{note.Id} - {note.Title}</li>"

[<HookComponent>]
let Notes () =
  let state, dispatch = Hook.useElmish (init, update)

  let updateTitle =
    fun (e: Event) -> e.target.Value |> SetTitle |> dispatch

  let updateBody =
    fun (e: Event) -> e.target.Value |> SetBody |> dispatch

  html
    $"""
    <form @submit={fun (e: Event) ->
                     e.preventDefault ()
                     dispatch Save}>
      <input
        type="text"
        name="title"
        placeholder="Title"
        required
        @input={updateTitle}
        @blur={updateTitle}
        />

      <input
        type="text"
        name="body"
        placeholder="Body"
        @input={updateBody}
        @blur={updateBody}
        />

        <button type="submit">Add</button>
    </form>
    <section>
      <ul>
      {LitBindings.repeat (
         state.Notes |> Seq.rev,
         (fun item -> $"{item.Id}"),
         noteTemplate
       )}
      </ul>
    </section>
  """
