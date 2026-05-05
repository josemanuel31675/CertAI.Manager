using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CertAI.Manager.Models;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<VceExam> VceExams { get; set; }

    public virtual DbSet<VceOption> VceOptions { get; set; }

    public virtual DbSet<VceQuestion> VceQuestions { get; set; }



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VceExam>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__VCE_Exam__3214EC07F897B1AB");

            entity.ToTable("VCE_Exams");

            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.CreatedAt).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.DurationMinutes).HasDefaultValue(90);
            entity.Property(e => e.IsArchived).HasDefaultValue(false);
            entity.Property(e => e.IsFavorite).HasDefaultValue(false);
            entity.Property(e => e.SyncedAt).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(500);
        });

        modelBuilder.Entity<VceOption>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__VCE_Opti__3214EC07365AC6E7");

            entity.ToTable("VCE_Options");

            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.IsCorrect).HasDefaultValue(false);
            entity.Property(e => e.QuestionId).HasMaxLength(36);
            entity.Property(e => e.TextEn).HasColumnName("Text_EN");

            entity.HasOne(d => d.Question).WithMany(p => p.VceOptions)
                .HasForeignKey(d => d.QuestionId)
                .HasConstraintName("FK__VCE_Optio__Quest__787EE5A0");
        });

        modelBuilder.Entity<VceQuestion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__VCE_Ques__3214EC077D3A2BC3");

            entity.ToTable("VCE_Questions");

            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.ExamId).HasMaxLength(36);
            entity.Property(e => e.TextEn).HasColumnName("Text_EN");

            entity.HasOne(d => d.Exam).WithMany(p => p.VceQuestions)
                .HasForeignKey(d => d.ExamId)
                .HasConstraintName("FK__VCE_Quest__ExamI__74AE54BC");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
