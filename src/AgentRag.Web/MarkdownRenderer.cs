// The chat model answers in Markdown (lists, **bold**, tables). This turns it into HTML for the page.
//
// DisableHtml: any raw HTML inside the answer is shown as text, never run in the browser.

using Markdig;
using Microsoft.AspNetCore.Components;

namespace AgentRag.Web;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()   // tables, task lists, auto links, ...
        .DisableHtml()
        .Build();

    public static MarkupString ToHtml(string markdown)
    {
        return new MarkupString(Markdown.ToHtml(markdown ?? string.Empty, Pipeline));
    }
}
