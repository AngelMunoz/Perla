#r "nuget: Markdig, 0.41.3"
#r "nuget: Perla.Plugins, 1.0.0-beta-031"

open Perla.Plugins
open Markdig

let pipeline =
  lazy
    (MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      .UsePreciseSourceLocation()
      .Build())

let shouldProcess: FilePredicate =
  fun extension -> [ ".md"; ".markdown" ] |> List.contains extension

let transform: Transform =
  fun args -> {
    args with
        content = Markdown.ToHtml(args.content, pipeline.Value)
        extension = ".html"
  }

plugin "markdown-plugin" {
  should_process_file shouldProcess
  with_transform transform
}
