using System.Text;
using System.Text.Json;
using static CertAI.Manager.Controllers.TrainerController;

public class AzureOpenAIService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _deploymentName;

    public AzureOpenAIService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["AzureOpenAI:ApiKey"] ?? string.Empty;
        _endpoint = configuration["AzureOpenAI:Endpoint"] ?? string.Empty;
        _deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? string.Empty;
    }

    public async Task<RespuestaIA> GenerarRespuestaAsync(string promptSistema, string mensajeUsuario)
    {
        try
        {
            // URL de Azure OpenAI: {endpoint}/openai/deployments/{deployment-id}/chat/completions?api-version=2024-02-15-preview
            var url = $"{_endpoint.TrimEnd('/')}/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-02-15-preview";

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "system", content = promptSistema },
                    new { role = "user", content = mensajeUsuario }
                },
                max_tokens = 800,
                temperature = 0.7
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new RespuestaIA { Letra = "!", Detalle = $"Azure Error {(int)response.StatusCode}: {errorBody}" };
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            
            var botText = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            // Limpieza básica del JSON
            botText = botText.Replace("```json", "").Replace("```", "").Trim();
            int inicio = botText.IndexOf('{');
            int fin = botText.LastIndexOf('}');
            if (inicio != -1 && fin != -1 && fin > inicio)
            {
                botText = botText.Substring(inicio, (fin - inicio) + 1);
            }

            return JsonSerializer.Deserialize<RespuestaIA>(botText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new RespuestaIA { Letra = "!", Detalle = "Azure JSON Vacío" };
        }
        catch (Exception ex)
        {
            return new RespuestaIA { Letra = "!", Detalle = "Excepción Azure: " + ex.Message };
        }
    }
}
