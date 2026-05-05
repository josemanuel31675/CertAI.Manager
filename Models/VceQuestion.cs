using System;
using System.Collections.Generic;

namespace CertAI.Manager.Models;

public partial class VceQuestion
{
    public string Id { get; set; } = null!;

    public string? ExamId { get; set; } = null!;

    public string Text { get; set; } = null!;

    public string? Explanation { get; set; }

    public string? TextEn { get; set; }

    public virtual VceExam Exam { get; set; } = null!;

    [System.Text.Json.Serialization.JsonIgnore] // <--- Agrega esto
    public virtual ICollection<VceOption> VceOptions { get; set; } = new List<VceOption>();

    // Añade esta línea para resolver el error
    public string? Source { get; set; }
}
