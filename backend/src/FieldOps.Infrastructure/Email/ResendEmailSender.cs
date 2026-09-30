using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FieldOps.Application.Features.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends through the Resend HTTP API with a bearer key. A transport error or
/// a non-success status throws; only the provider and the status code (or
/// the failure category) are logged, never the response text, recipient,
/// bodies or the API key.
/// </summary>
internal sealed class ResendEmailSender(
    HttpClient httpClient,
    IOptions<EmailSettings> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public static readonly Uri Endpoint = new("https://api.resend.com/emails");

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                $"{settings.SenderName} <{settings.SenderAddress}>",
                [message.To],
                message.Subject,
                message.TextBody,
                message.HtmlBody)),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Resend.ApiKey);

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Email delivery failed: {Provider} {FailureCategory}",
                EmailSettings.ResendProvider,
                ex.GetType().Name);

            throw new EmailDeliveryException(EmailSettings.ResendProvider, null);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;

                logger.LogError(
                    "Email delivery failed: {Provider} {StatusCode}",
                    EmailSettings.ResendProvider,
                    statusCode);

                throw new EmailDeliveryException(EmailSettings.ResendProvider, statusCode);
            }
        }
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html")] string Html);
}
