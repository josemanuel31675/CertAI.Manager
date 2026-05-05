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
        var requestBody = new
        {
            contents = new[] {
                new { role = "user", parts = new[] { new { text = $"{promptSistema}\n\nDictado: {mensajeUsuario}" } } }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"{_url}?key={_apiKey}", content);

        if (response.IsSuccessStatusCode)
        {
            var jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);
            var botText = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();

            // Dentro de GenerarRespuestaAsync, después de obtener botText:
            botText = botText.Replace("```json", "").Replace("```", "").Trim();

            // LOGICA ROBUSTA: Extraer solo lo que está entre llaves
            int inicio = botText.IndexOf('{');
            int fin = botText.LastIndexOf('}');

            if (inicio != -1 && fin != -1 && fin > inicio)
            {
                botText = botText.Substring(inicio, (fin - inicio) + 1);
            }

            Console.WriteLine("JSON Limpio para Deserializar: " + botText);

            try
            {
                return JsonSerializer.Deserialize<RespuestaIA>(botText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                return new RespuestaIA { Letra = "!", Detalle = "Error al leer JSON: " + ex.Message };
            }

        }

        return new RespuestaIA { Letra = "!", Detalle = "Error de conexión con la IA" };
    }
}