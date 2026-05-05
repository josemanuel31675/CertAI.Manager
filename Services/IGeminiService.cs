using static CertAI.Manager.Controllers.TrainerController;

namespace CertAI.Manager.Services
{
    public interface IGeminiService // <--- Cambiamos 'class' por 'interface'
    {
        // Definimos el contrato: qué recibe y qué devuelve
        Task<RespuestaIA> GenerarRespuestaAsync(string promptSistema, string mensajeUsuario);
    }
}
