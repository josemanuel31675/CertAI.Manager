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
            public string? Pregunta { get; set; }
            public List<string>? Opciones { get; set; }
        }

        public class CorreccionRequest
        {
            // ID de la pregunta en la BD (puede ser null si es nueva)
            public string? QuestionId { get; set; }
            // Texto de la pregunta (para buscarla si no hay ID)
            public string? PreguntaTexto { get; set; }
            // Lista de textos de las opciones que el usuario marcó como CORRECTAS
            public List<string>? OpcionesCorrectas { get; set; }
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

            // BÚSQUEDA BILINGÜE POR PALABRAS CLAVE: Cargamos preguntas en memoria para una coincidencia flexible que ignore ruidos/errores de transcripción
            VceQuestion? matchedQuestion = null;
            if (data.Pregunta.Length > 10)
            {
                string search = data.Pregunta.ToLower();
                
                // Lista de palabras vacías (stop words) comunes en preguntas técnicas de TI.
                // Filtrar estas palabras previene falsos positivos basados en vocabulario genérico como "servicio", "proporciona", "plataforma", etc.
                var stopWords = new HashSet<string> { 
                    "para", "como", "tiene", "ayuda", "usar", "servicio", "servicios", 
                    "tipo", "tipos", "proporciona", "proporcionan", "plataforma", "plataformas",
                    "sobre", "entre", "donde", "cuando", "quien", "desde", "hasta", "hacia",
                    "cual", "cuál", "cuales", "cuáles", "este", "esta", "estos", "estas"
                };

                // Tokenizamos el dictado en palabras significativas (palabras clave > 3 letras, excluyendo stop words)
                var keywords = search.Split(new[] { ' ', ',', '.', '?', '¿', '¡', '!', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(w => w.Length > 3 && !stopWords.Contains(w))
                                     .ToList();

                if (keywords.Any())
                {
                    // Obtenemos candidatos de la base de datos
                    var allQuestions = await _context.VceQuestions
                        .Include(q => q.VceOptions)
                        .Include(q => q.Exam)
                        .ToListAsync();

                    // Buscamos la pregunta que contenga la mayor cantidad de palabras clave reales
                    var scoredMatches = allQuestions
                        .Select(q => new
                        {
                            Question = q,
                            Score = keywords.Count(kw => 
                                (q.Text != null && q.Text.ToLower().Contains(kw)) || 
                                (q.TextEn != null && q.TextEn.ToLower().Contains(kw)))
                        })
                        .Where(x => x.Score > 0)
                        .OrderByDescending(x => x.Score)
                        .ToList();

                    // Incrementamos la exigencia del umbral para evitar falsos positivos:
                    // Exigimos que coincida al menos el 55% de las palabras clave únicas, con un mínimo estricto de 3 coincidencias.
                    var threshold = Math.Max(3, (int)(keywords.Count * 0.55));
                    var bestMatch = scoredMatches.FirstOrDefault(x => x.Score >= threshold);

                    if (bestMatch != null)
                    {
                        matchedQuestion = bestMatch.Question;
                    }
                }
            }

            // Si encontramos la pregunta en la DB, usamos sus opciones reales para la IA
            var opcionesParaIA = data.Opciones;
            string? correctLettersFromDb = null;
            string? correctTextsFromDb = null;

            if (matchedQuestion != null && matchedQuestion.VceOptions.Any())
            {
                // Combinamos/Preferimos las opciones de la DB por ser más exactas
                var optionsList = matchedQuestion.VceOptions.ToList();
                opcionesParaIA = optionsList.Select(o => o.Text).ToList();

                var correctIndices = new List<int>();
                var correctTexts = new List<string>();

                for (int i = 0; i < optionsList.Count; i++)
                {
                    if (optionsList[i].IsCorrect == true)
                    {
                        correctIndices.Add(i);
                        correctTexts.Add(optionsList[i].Text);
                    }
                }

                if (correctIndices.Any())
                {
                    // Convertimos índices a letras: 0 -> A, 1 -> B, 2 -> C, etc.
                    var letters = correctIndices.Select(idx => ((char)('A' + idx)).ToString());
                    correctLettersFromDb = string.Join(", ", letters);
                    correctTextsFromDb = string.Join(", ", correctTexts);
                }
            }

            // Si la base de datos ya sabe la respuesta correcta, le damos esa pista crucial a la IA
            // para que su explicación técnica esté perfectamente alineada y enfocada en el acierto real.
            string? dbHint = null;
            if (!string.IsNullOrEmpty(correctTextsFromDb) && !string.IsNullOrEmpty(correctLettersFromDb))
            {
                dbHint = $" [Nota: La respuesta o respuestas correctas confirmadas por la base de datos son: '{correctTextsFromDb}' (Letra: '{correctLettersFromDb}'). Debes elegir obligatoriamente esta o estas opciones en tu JSON, devolver sus letras correspondientes y explicar técnicamente por qué son las correctas.]";
            }

            // Pasamos el título del examen si existe y la pista de la base de datos
            var respuestaIA = await LlamarIA(data.Pregunta, opcionesParaIA, matchedQuestion?.Exam?.Title, dbHint);

            // SOBREESCRITURA CON BASE DE DATOS (GARANTÍA 100%): Si la pregunta proviene de la base de datos, 
            // nos aseguramos de que el resultado final use exactamente las respuestas marcadas como correctas en SQL,
            // evitando cualquier posible alucinación o error de mapeo de la IA.
            if (!string.IsNullOrEmpty(correctLettersFromDb) && !string.IsNullOrEmpty(correctTextsFromDb))
            {
                respuestaIA.Letra = correctLettersFromDb;
                respuestaIA.OpcionCorrectaTexto = correctTextsFromDb;
            }

            // AUTO-GUARDADO: Si la pregunta no estaba en la DB (incluso tras búsqueda flexible), la insertamos para futuras consultas
            if (matchedQuestion == null)
            {
                try
                {
                    // Buscamos o creamos un examen por defecto para almacenar estas preguntas
                    var defaultExam = await _context.VceExams.FirstOrDefaultAsync();
                    if (defaultExam == null)
                    {
                        defaultExam = new VceExam
                        {
                            Id = Guid.NewGuid().ToString(),
                            Title = "Preguntas Guardadas por IA",
                            Description = "Banco de preguntas autogeneradas e insertadas automáticamente por la Inteligencia Artificial"
                        };
                        _context.VceExams.Add(defaultExam);
                        await _context.SaveChangesAsync();
                    }

                    var newQuestion = new VceQuestion
                    {
                        Id = Guid.NewGuid().ToString(),
                        ExamId = defaultExam.Id,
                        Text = data.Pregunta,
                        Explanation = respuestaIA.Detalle,
                        Source = "Azure AI Engine"
                    };

                    _context.VceQuestions.Add(newQuestion);

                    if (respuestaIA.OpcionesSugeridas != null && respuestaIA.OpcionesSugeridas.Any())
                    {
                        foreach (var optText in respuestaIA.OpcionesSugeridas)
                        {
                            // Verificamos si la opción generada por la IA es la correcta
                            var esCorrecta = respuestaIA.OpcionCorrectaTexto != null && (
                                optText.ToLower().Contains(respuestaIA.OpcionCorrectaTexto.ToLower()) ||
                                respuestaIA.OpcionCorrectaTexto.ToLower().Contains(optText.ToLower())
                            );

                            var newOption = new VceOption
                            {
                                Id = Guid.NewGuid().ToString(),
                                QuestionId = newQuestion.Id,
                                Text = optText,
                                IsCorrect = esCorrecta
                            };
                            _context.VceOptions.Add(newOption);
                        }
                    }

                    await _context.SaveChangesAsync();
                    matchedQuestion = newQuestion; // Asignamos para reflejar el guardado en el JSON final
                }
                catch (Exception ex)
                {
                    // Manejo silencioso de errores para que la aplicación responda al usuario aunque falle la base de datos
                    System.Diagnostics.Debug.WriteLine($"[AutoSave Error] No se pudo guardar en la base de datos: {ex.Message}");
                }
            }

            // Construimos la lista de opciones final para el JSON
            var todasLasOpciones = new List<object>();
            if (matchedQuestion != null && matchedQuestion.VceOptions != null && matchedQuestion.VceOptions.Any())
            {
                todasLasOpciones.AddRange(matchedQuestion.VceOptions.Select(o => new { es = o.Text ?? "", en = o.TextEn ?? "" }));
            }
            else if (respuestaIA.OpcionesSugeridas != null)
            {
                todasLasOpciones.AddRange(respuestaIA.OpcionesSugeridas.Select(o => new { es = o ?? "", en = "" }));
            }

            return Json(new
            {
                letra = respuestaIA.Letra,
                fuente = matchedQuestion != null && matchedQuestion.Source != "Azure AI Engine" ? $"Base de Datos + IA ({matchedQuestion.Exam?.Title ?? "Desconocido"})" : "Azure AI Engine (Autoguardado)",
                questionId = matchedQuestion?.Id,  // <--- ID para corrección manual
                preguntaTexto = matchedQuestion?.Text ?? data.Pregunta,
                preguntaEn = matchedQuestion?.TextEn,
                todasLasOpciones,
                opcionCorrectaTexto = respuestaIA.OpcionCorrectaTexto,
                detalle = respuestaIA.Detalle
            });
        }

        private async Task<RespuestaIA> LlamarIA(string? textoDictado, List<string>? opcionesDictadas, string? examTitle, string? dbHint = null)
        {
            if (string.IsNullOrEmpty(textoDictado)) return new RespuestaIA { Letra = "!", Detalle = "Dictado vacío" };
            
            // Si la pregunta coincide con un examen de la DB, nos enfocamos en esa certificación.
            // Si no hay match, le pedimos a la IA que deduzca la certificación de TI basándose en el contenido de la pregunta.
            string contextoExamen = !string.IsNullOrEmpty(examTitle) 
                ? $"la certificación '{examTitle}'" 
                : "la certificación de TI o Nube correspondiente al tema de la pregunta (por ejemplo: AZ-900, AI-900, DP-900, AZ-104, AWS, etc.)";

            // 1. Preparamos el mensaje para la IA
            string promptSistema = $@"Actúa como un experto certificado en {contextoExamen}.
                            Tu objetivo es procesar un dictado de voz que puede contener ruidos, palabras mal interpretadas o errores fonéticos.

                            FLUJO DE TRABAJO:
                            1. LIMPIEZA CONTEXTUAL: Analiza el dictado y corrige las palabras que no tengan sentido técnico, sustituyéndolas por el término técnico correcto más probable en el contexto de dicha certificación de TI (ej. errores de transcripción de voz).
                            2. EXTRACCIÓN: Identifica la pregunta raíz y todas las opciones de respuesta del examen.
                            3. RESOLUCIÓN: Determina la respuesta o respuestas correctas basadas estrictamente en el plan de estudios y la documentación oficial de dicha certificación.
                            4. RESPUESTA: Genera un JSON con esta estructura exacta:
                            {{
                              ""Letra"": ""letra_elegida"",
                              ""OpcionCorrectaTexto"": ""texto_exacto_de_la_opcion"",
                              ""Detalle"": ""explicación técnica concisa"",
                              ""OpcionesSugeridas"": [""lista_de_todas_las_opciones_encontradas""]
                            }}

                            REGLAS ADICIONALES IMPORTANTES:
                            - MÚLTIPLES RESPUESTAS: Si la pregunta requiere seleccionar más de una respuesta correcta (ej. ""¿Qué dos modelos...""), coloca todas las letras de opciones correctas en ""Letra"" separadas por comas (ej. ""C, D"") y concatena sus textos en ""OpcionCorrectaTexto"" separados por comas (ej. ""Celebridades, Puntos de referencia"").
                            - REGLA DE ORO: Si el dictado es ambiguo, prioriza la interpretación técnica más coherente dentro de la nube (como Microsoft Azure) o el ecosistema de TI correspondiente.";

            string mensajeUsuario = $"Dictado: {textoDictado}.";
            if (opcionesDictadas != null && opcionesDictadas.Any())
            {
                mensajeUsuario += $" Opciones proporcionadas: {string.Join(" | ", opcionesDictadas)}";
            }
            if (!string.IsNullOrEmpty(dbHint))
            {
                mensajeUsuario += dbHint;
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

        /// <summary>
        /// Permite al usuario corregir manualmente las respuestas correctas de una pregunta
        /// directamente desde la pantalla de revisión, actualizando la base de datos.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CorregirRespuesta([FromBody] CorreccionRequest data)
        {
            if (data == null || (string.IsNullOrEmpty(data.QuestionId) && string.IsNullOrEmpty(data.PreguntaTexto)))
                return BadRequest(new { ok = false, mensaje = "Se requiere el ID o texto de la pregunta." });

            if (data.OpcionesCorrectas == null || !data.OpcionesCorrectas.Any())
                return BadRequest(new { ok = false, mensaje = "Debes seleccionar al menos una opción correcta." });

            try
            {
                VceQuestion? question = null;

                // 1. Buscamos por ID directo si viene
                if (!string.IsNullOrEmpty(data.QuestionId))
                {
                    question = await _context.VceQuestions
                        .Include(q => q.VceOptions)
                        .FirstOrDefaultAsync(q => q.Id == data.QuestionId);
                }

                // 2. Si no hay ID o no se encontró, buscamos por similitud de texto
                if (question == null && !string.IsNullOrEmpty(data.PreguntaTexto))
                {
                    var allQuestions = await _context.VceQuestions
                        .Include(q => q.VceOptions)
                        .ToListAsync();

                    question = allQuestions.FirstOrDefault(q =>
                        q.Text != null && q.Text.ToLower().Contains(data.PreguntaTexto.ToLower().Substring(0, Math.Min(30, data.PreguntaTexto.Length))));
                }

                if (question == null)
                    return NotFound(new { ok = false, mensaje = "No se encontró la pregunta en la base de datos." });

                // 3. Normalizamos los textos correctos para comparación flexible
                var textosCorrecto = data.OpcionesCorrectas
                    .Select(t => t.Trim().ToLower())
                    .ToList();

                int actualizadas = 0;
                foreach (var option in question.VceOptions)
                {
                    var textoOpt = option.Text?.Trim().ToLower() ?? "";
                    // Marcamos como correcta si el texto coincide (parcialmente) con alguna selección del usuario
                    bool debeSerCorrecta = textosCorrecto.Any(tc =>
                        textoOpt.Contains(tc) || tc.Contains(textoOpt));

                    if (option.IsCorrect != debeSerCorrecta)
                    {
                        option.IsCorrect = debeSerCorrecta;
                        actualizadas++;
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = $"Corrección guardada: {actualizadas} opción(es) actualizada(s).",
                    questionId = question.Id
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, mensaje = $"Error al guardar: {ex.Message}" });
            }
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
            public string? Letra { get; set; }

            // Explicación técnica de por qué esa es la respuesta (útil para estudiar)
            public string? Detalle { get; set; }

            // La respuesta correcta en texto (ej: "Sistema de seguridad")
            public string? OpcionCorrectaTexto { get; set; }

            // Las opciones que la IA identificó en el dictado o generó
            public List<string>? OpcionesSugeridas { get; set; } = new List<string>();
        }




    }
}
