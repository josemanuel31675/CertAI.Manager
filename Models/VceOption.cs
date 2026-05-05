using System;
using System.Collections.Generic;

namespace CertAI.Manager.Models;

public partial class VceOption
{
    public string Id { get; set; } = null!;

    public string QuestionId { get; set; } = null!;

    public string Text { get; set; } = null!;

    public bool? IsCorrect { get; set; }

    public string? TextEn { get; set; }

    public virtual VceQuestion Question { get; set; } = null!;
}
