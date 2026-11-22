// Every call from the web app to the API, in one class. The pages never build URLs or JSON themselves.
//
//   GET    api/documents                  ListDocumentsAsync
//   POST   api/documents                  UploadAsync
//   POST   api/documents/{id}/reprocess   ReprocessAsync
//   DELETE api/documents/{id}             DeleteAsync
//   POST   api/chat                       AskAsync

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;

namespace AgentRag.Web;

public sealed class RagApiClient
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;   // same limit as the API

    private readonly HttpClient _http;

    // The HttpClient comes from Program.cs, already pointed at the API.
    public RagApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DocumentDto>> ListDocumentsAsync(CancellationToken cancellationToken)
    {
        List<DocumentDto>? documents = await _http.GetFromJsonAsync<List<DocumentDto>>("api/documents", cancellationToken);
        return documents ?? new List<DocumentDto>();
    }

    /// <summary>Uploads ONE file. The API saves it and returns at once; embeddings are made in the background.</summary>
    public async Task<UploadResultDto> UploadAsync(IBrowserFile file, CancellationToken cancellationToken)
    {
        using (MultipartFormDataContent form = new MultipartFormDataContent())
        {
            StreamContent content = new StreamContent(file.OpenReadStream(MaxFileSizeBytes, cancellationToken));
            string contentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, "files", file.Name);

            using (HttpResponseMessage response = await _http.PostAsync("api/documents", form, cancellationToken))
            {
                await EnsureSuccessAsync(response, cancellationToken);

                List<UploadResultDto>? results = await response.Content.ReadFromJsonAsync<List<UploadResultDto>>(cancellationToken);
                if (results == null || results.Count == 0)
                {
                    return new UploadResultDto { FileName = file.Name, Message = "The API returned no result." };
                }

                return results[0];
            }
        }
    }

    public async Task ReprocessAsync(int id, CancellationToken cancellationToken)
    {
        using (HttpResponseMessage response = await _http.PostAsync("api/documents/" + id + "/reprocess", null, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using (HttpResponseMessage response = await _http.DeleteAsync("api/documents/" + id, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task<ChatAnswerDto> AskAsync(string message, List<ChatTurnDto> history, CancellationToken cancellationToken)
    {
        ChatRequestDto request = new ChatRequestDto { Message = message, History = history };

        using (HttpResponseMessage response = await _http.PostAsJsonAsync("api/chat", request, cancellationToken))
        {
            await EnsureSuccessAsync(response, cancellationToken);

            ChatAnswerDto? answer = await response.Content.ReadFromJsonAsync<ChatAnswerDto>(cancellationToken);
            return answer ?? new ChatAnswerDto();
        }
    }

    // Like EnsureSuccessStatusCode, but with the API's own error message (ProblemDetails.detail) when there is one.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = "The API returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ".";
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            ProblemDetails? problem = System.Text.Json.JsonSerializer.Deserialize<ProblemDetails>(
                body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                message = problem.Detail;
            }
        }
        else if (!string.IsNullOrWhiteSpace(body))
        {
            message = body.Trim('"');
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }
}
