using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CertAI.Manager.Models;

namespace CertAI.Manager.Controllers // Asegúrate que el namespace sea correcto
{
    public class TrainerController : Controller
{
    private readonly AppDbContext _context;
    private readonly IGeminiService _geminiService; // <--- AQUÍ SE DECLARA

        public class ConsultaPregunta
        {
            public string Pregunta { get; set; }
            public List<string> Opciones { get; set; }
        }

        public TrainerController(IGeminiService geminiService, AppDbContext context)
        {
            _geminiService = geminiService;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // El .AsNoTracking() ayuda a que sea más rápido para solo lectura
                var question = await _context.VceQuestions
                    .Include(q => q.VceOptions)
                    .FirstOrDefaultAsync(); // Trae la primera que encuentre para probar

                if (question == null)
                {
                    return Content("Conectado a SQL, pero la tabla VceQuestions está VACÍA.");
                }

                return View(question);
            }
            catch (Exception ex)
            {
                // Si hay un error de conexión, esto te lo dirá en el navegador
                return Content($"Error de SQL: {ex.Message} --- Inner: {ex.InnerException?.Message}");
            }
        }


        [HttpPost]
        public async Task<IActionResult> ProcesarPregunta([FromBody] ConsultaPregunta data)
        {
            if (string.IsNullOrEmpty(data.Pregunta))
            {
                return BadRequest("El dictado está vacío.");
            }

            // BÚSQUEDA BILINGÜE: Intentamos encontrar la pregunta en la DB si el dictado es lo suficientemente largo
            VceQuestion? matchedQuestion = null;
            if (data.Pregunta.Length > 10)
            {
                string search = data.Pregunta.ToLower();
                matchedQuestion = await _context.VceQuestions
                    .Include(q => q.VceOptions)
                    .FirstOrDefaultAsync(q => 
                        (q.Text != null && q.Text.ToLower().Contains(search)) || 
                        (q.TextEn != null && q.TextEn.ToLower().Contains(search)));
            }

            // Si encontramos la pregunta en la DB, usamos sus opciones reales para la IA
            var opcionesParaIA = data.Opciones;
            if (matchedQuestion != null && matchedQuestion.VceOptions.Any())
            {
                // Combinamos/Preferimos las opciones de la DB por ser más exactas
                opcionesParaIA = matchedQuestion.VceOptions.Select(o => o.Text).ToList();
            }

            var respuestaIA = await LlamarIA(data.Pregunta, opcionesParaIA);

            return Json(new
            {
                letra = respuestaIA.Letra,
                fuente = matchedQuestion != null ? $"Base de Datos + IA" : "Azure AI Engine",
                preguntaTexto = matchedQuestion?.Text ?? data.Pregunta,
                preguntaEn = matchedQuestion?.TextEn,
                todasLasOpciones = matchedQuestion != null 
                    ? matchedQuestion.VceOptions.Select(o => new { es = o.Text, en = o.TextEn }).ToList()
                    : respuestaIA.OpcionesSugeridas.Select(o => new { es = o, en = "" }).ToList(),
                opcionCorrectaTexto = respuestaIA.OpcionCorrectaTexto,
                detalle = respuestaIA.Detalle
            });
        }



        private async Task<RespuestaIA> LlamarIA(string textoDictado, List<string> opcionesDictadas)
        {
            // 1. Preparamos el mensaje para la IA
            string promptSistema = @"Actúa como un experto certificado en Microsoft Azure AI-900.
                            Tu objetivo es procesar un dictado de voz que puede contener ruidos, palabras mal interpretadas o errores fonéticos.

                            FLUJO DE TRABAJO:
                            1. LIMPIEZA CONTEXTUAL: Analiza el dictado y corrige las palabras que no tengan sentido técnico, sustituyéndolas por el término correcto de Azure AI-900 más probable (ej. errores de transcripción de voz).
                            2. EXTRACCIÓN: Identifica la pregunta raíz y todas las opciones presentadas.
                            3. RESOLUCIÓN: Determina la respuesta correcta basada estrictamente en la documentación de Microsoft.
                            4. RESPUESTA: Genera un JSON con esta estructura exacta:
                            {
                              ""Letra"": ""letra_elegida"",
                              ""OpcionCorrectaTexto"": ""texto_exacto_de_la_opcion"",
                              ""Detalle"": ""explicación técnica concisa"",
                              ""OpcionesSugeridas"": [""lista_de_todas_las_opciones_encontradas""]
                            }

                            REGLA DE ORO: Si el dictado es ambiguo, prioriza los conceptos de Inteligencia Artificial Generativa, Filtros de Contenido y Principios de IA de Microsoft.";

            string mensajeUsuario = $"Dictado: {textoDictado}.";
            if (opcionesDictadas != null && opcionesDictadas.Any())
            {
                mensajeUsuario += $" Opciones proporcionadas: {string.Join(" | ", opcionesDictadas)}";
            }

            // 2. Llamada real al SDK
            var resultadoIA = await _geminiService.GenerarRespuestaAsync(promptSistema, mensajeUsuario);

            // Si la IA no encontró opciones pero el usuario sí envió, mantenemos las del usuario
            if ((resultadoIA.OpcionesSugeridas == null || !resultadoIA.OpcionesSugeridas.Any()) && opcionesDictadas != null)
            {
                resultadoIA.OpcionesSugeridas = opcionesDictadas;
            }

            return resultadoIA;
        }

        public interface IGeminiService
        {
            // Este es el método que procesará tu dictado sucio y devolverá el JSON limpio
            Task<RespuestaIA> GenerarRespuestaAsync(string promptSistema, string mensajeUsuario);
        }

        // Clase de apoyo (puedes ponerla al final del archivo del controlador)
        public class RespuestaIA
        {
            // "A", "B", "C", "D" o "S" para indicar el estado
            public string Letra { get; set; }

            // Explicación técnica de por qué esa es la respuesta (útil para estudiar)
            public string Detalle { get; set; }

            // La respuesta correcta en texto (ej: "Sistema de seguridad")
            public string OpcionCorrectaTexto { get; set; }

            // Las opciones que la IA identificó en el dictado o generó
            public List<string> OpcionesSugeridas { get; set; } = new List<string>();
        }




    }
}
