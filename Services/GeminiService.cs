using System.Text;
using System.Text.Json;
using static CertAI.Manager.Controllers.TrainerController;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _url;

    public GeminiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _url = configuration["Gemini:ApiUrl"] ?? string.Empty;
    }

    public async Task<RespuestaIA> GenerarRespuestaAsync(string promptSistema, string mensajeUsuario)
    {
        try 
        {
            var requestBody = new
            {
                contents = new[] {
                    new { role = "user", parts = new[] { new { text = $"{promptSistema}\n\nDictado: {mensajeUsuario}" } } }
                },
                safetySettings = new[] {
                    new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                    new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_url}?key={_apiKey}", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new RespuestaIA { Letra = "!", Detalle = $"API Error {(int)response.StatusCode}: {errorBody}" };
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return new RespuestaIA { Letra = "!", Detalle = "Sin candidatos (Bloqueo de seguridad)." };
            }

            var botText = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
            botText = botText.Replace("```json", "").Replace("```", "").Trim();

            int inicio = botText.IndexOf('{');
            int fin = botText.LastIndexOf('}');
            if (inicio != -1 && fin != -1 && fin > inicio)
            {
                botText = botText.Substring(inicio, (fin - inicio) + 1);
            }

            return JsonSerializer.Deserialize<RespuestaIA>(botText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new RespuestaIA { Letra = "!", Detalle = "JSON Null" };
        }
        catch (Exception ex)
        {
            return new RespuestaIA { Letra = "!", Detalle = "Excepción interna: " + ex.Message };
        }
    }
}