namespace App

open Feliz
open Feliz.Router

module Routing =
    /// <summary>
    /// Page discriminated union for typed routing
    /// </summary>
    type Page =
        | Home
        | Hello
        | Counter
        | NotFound

    // string list -> Page
    let parseUrl = function
        | [] -> Page.Home
        | [ "hello" ] -> Page.Hello
        | [ "counter" ] -> Page.Counter
        | _ -> Page.NotFound

type Components =
    /// <summary>
    /// The simplest possible React component.
    /// Shows a header with the text Hello World
    /// </summary>
    [<ReactComponent>]
    static member HelloWorld() = Html.h1 "Hello World"

    /// <summary>
    /// A stateful React component that maintains a counter
    /// </summary>
    [<ReactComponent>]
    static member Counter() =
        let (count, setCount) = React.useState(0)
        Html.div [
            Html.h1 count
            Html.button [
                prop.onClick (fun _ -> setCount(count + 1))
                prop.text "Increment"
            ]
        ]

    /// <summary>
    /// A simple toolbar with navigation links
    /// </summary>
    [<ReactComponent>]
    static member Toolbar() =
        Html.div [
            prop.className "toolbar"
            prop.children [
                Html.a [ prop.className "toolbar-link"; prop.href "/"; prop.text "Index" ]
                Html.a [ prop.className "toolbar-link"; prop.href "/hello"; prop.text "Hello" ]
                Html.a [ prop.className "toolbar-link"; prop.href "/counter"; prop.text "Counter" ]
            ]
        ]

    /// <summary>
    /// A React component that uses Feliz.Router
    /// to determine what to show based on the current URL
    /// </summary>
    [<ReactComponent>]
    static member Router() =
        let page = Routing.parseUrl(Router.currentPath())
        let currentPage =
            match page with
            | Routing.Page.Home -> Html.h1 "Index"
            | Routing.Page.Hello -> Components.HelloWorld()
            | Routing.Page.Counter -> Components.Counter()
            | Routing.Page.NotFound -> Html.h1 "Not found"
        Html.div [
            prop.className "page-container"
            prop.children [
                Components.Toolbar()
                React.router [
                    router.onUrlChanged (fun _ -> ()) // No need to update state
                    router.children currentPage
                ]
            ]
        ]
