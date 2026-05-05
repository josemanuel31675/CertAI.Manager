using System;
using System.Collections.Generic;

namespace CertAI.Manager.Models;

public partial class VceExam
{
    public string Id { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int? DurationMinutes { get; set; }

    public bool? IsFavorite { get; set; }

    public bool? IsArchived { get; set; }

    public string? CreatedAt { get; set; }

    public string? SyncedAt { get; set; }

    public virtual ICollection<VceQuestion> VceQuestions { get; set; } = new List<VceQuestion>();
}
