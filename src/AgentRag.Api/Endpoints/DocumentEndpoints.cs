// HTTP endpoints for documents. Each handler only translates HTTP <-> a class call; no logic here.
//
//   POST   /api/documents                 multipart/form-data, field "files" (one or more)
//   GET    /api/documents                 list with status
//   POST   /api/documents/{id}/reprocess  back to Pending
//   DELETE /api/documents/{id}            delete file + chunks

using AgentRag.Api.Database;
using AgentRag.Api.Ingestion;

namespace AgentRag.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/documents");

        // The web app calls this server-to-server (no browser form), so no antiforgery token is involved.
        group.MapPost("/", UploadAsync).DisableAntiforgery();
        group.MapGet("/", ListAsync);
        group.MapPost("/{id:int}/reprocess", ReprocessAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> UploadAsync(IFormFileCollection files, DocumentUploader uploader, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return Results.BadRequest("Send one or more files in the form field 'files'.");
        }

        List<UploadResult> results = new List<UploadResult>();
        foreach (IFormFile file in files)
        {
            byte[] content;
            using (MemoryStream buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            results.Add(await uploader.UploadAsync(file.FileName, file.ContentType, content, cancellationToken));
        }

        return Results.Ok(results);
    }

    private static async Task<IResult> ListAsync(DocumentRepository documents, CancellationToken cancellationToken)
    {
        return Results.Ok(await documents.ListAsync(cancellationToken));
    }

    private static async Task<IResult> ReprocessAsync(int id, DocumentRepository documents, CancellationToken cancellationToken)
    {
        bool found = await documents.ResetToPendingAsync(id, cancellationToken);
        return found ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteAsync(int id, DocumentRepository documents, CancellationToken cancellationToken)
    {
        bool found = await documents.DeleteAsync(id, cancellationToken);
        return found ? Results.NoContent() : Results.NotFound();
    }
}
