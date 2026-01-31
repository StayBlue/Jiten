using Jiten.Core.Data;
using Jiten.Core.Data.JMDict;
using Microsoft.Extensions.Configuration;

namespace Jiten.Core;

using Microsoft.EntityFrameworkCore;

public class JitenDbContext : DbContext
{
    public DbContextOptions<JitenDbContext>? DbOptions { get; set; }

    public DbSet<Deck> Decks { get; set; }
    public DbSet<DeckWord> DeckWords { get; set; }
    public DbSet<DeckRawText> DeckRawTexts { get; set; }
    public DbSet<DeckTitle> DeckTitles { get; set; }
    public DbSet<DeckStats> DeckStats { get; set; }
    public DbSet<DeckDifficulty> DeckDifficulties { get; set; }

    public DbSet<JmDictWord> JMDictWords { get; set; }
    public DbSet<JmDictWordFrequency> JmDictWordFrequencies { get; set; }
    public DbSet<JmDictDefinition> Definitions { get; set; }
    public DbSet<JmDictLookup> Lookups { get; set; }
    public DbSet<Kanji> Kanjis { get; set; }
    public DbSet<WordKanji> WordKanjis { get; set; }
    
    public DbSet<ExampleSentence> ExampleSentences { get; set; }
    public DbSet<ExampleSentenceWord> ExampleSentenceWords { get; set; }

    public DbSet<Tag> Tags { get; set; }
    public DbSet<DeckGenre> DeckGenres { get; set; }
    public DbSet<DeckTag> DeckTags { get; set; }

    public DbSet<ExternalGenreMapping> ExternalGenreMappings { get; set; }
    public DbSet<ExternalTagMapping> ExternalTagMappings { get; set; }

    public DbSet<DeckRelationship> DeckRelationships { get; set; }

    public DbSet<WordSet> WordSets { get; set; }
    public DbSet<WordSetMember> WordSetMembers { get; set; }

    public JitenDbContext()
    {
    }

    public JitenDbContext(DbContextOptions<JitenDbContext> options) : base(options)
    {
        DbOptions = options;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("fuzzystrmatch");

        modelBuilder.HasDefaultSchema("jiten"); // Set a default schema

        modelBuilder.Entity<Deck>(entity =>
        {
            entity.Property(d => d.DeckId)
                  .ValueGeneratedOnAdd();

            entity.Property(d => d.ParentDeckId)
                  .HasDefaultValue(null);

            entity.Property(d => d.OriginalTitle)
                  .HasMaxLength(200);

            entity.Property(d => d.RomajiTitle)
                  .HasMaxLength(200);

            entity.Property(d => d.EnglishTitle)
                  .HasMaxLength(200);

            entity.HasMany(d => d.Links)
                  .WithOne(l => l.Deck)
                  .HasForeignKey(l => l.DeckId);

            entity.HasIndex(d => d.OriginalTitle).HasDatabaseName("IX_OriginalTitle");
            entity.HasIndex(d => d.RomajiTitle).HasDatabaseName("IX_RomajiTitle");
            entity.HasIndex(d => d.EnglishTitle).HasDatabaseName("IX_EnglishTitle");
            entity.HasIndex(d => d.MediaType).HasDatabaseName("IX_MediaType");
            entity.HasIndex(d => d.CharacterCount).HasDatabaseName("IX_CharacterCount");
            entity.HasIndex(d => d.ReleaseDate).HasDatabaseName("IX_ReleaseDate");
            entity.HasIndex(d => d.UniqueKanjiCount).HasDatabaseName("IX_UniqueKanjiCount");
            entity.HasIndex(d => d.Difficulty).HasDatabaseName("IX_Difficulty");
            entity.HasIndex(d => d.ExternalRating).HasDatabaseName("IX_ExternalRating");
            entity.HasIndex(d => new { d.ParentDeckId, d.MediaType }).HasDatabaseName("IX_ParentDeckId_MediaType");

            entity.HasOne(d => d.ParentDeck)
                  .WithMany(p => p.Children)
                  .HasForeignKey(d => d.ParentDeckId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
        
        modelBuilder.Entity<DeckTitle>(entity =>
        {
              entity.HasKey(dt => dt.DeckTitleId);
              entity.Property(dt => dt.Title).IsRequired().HasMaxLength(200);
              entity.Property(dt => dt.TitleType).IsRequired();

              // TitleNoSpaces is a generated column computed by PostgreSQL
              entity.Property(dt => dt.TitleNoSpaces)
                    .HasComputedColumnSql("REPLACE(\"Title\", ' ', '')", stored: true);

              entity.HasOne(dt => dt.Deck)
                    .WithMany(d => d.Titles)
                    .HasForeignKey(dt => dt.DeckId)
                    .OnDelete(DeleteBehavior.Cascade);

              entity.HasIndex(dt => dt.Title).HasDatabaseName("IX_DeckTitles_Title");
              entity.HasIndex(dt => new { dt.DeckId, dt.TitleType }).HasDatabaseName("IX_DeckTitles_DeckId_TitleType");
        });

        modelBuilder.Entity<DeckWord>(entity =>
        {
            entity.Property(d => d.DeckWordId)
                  .ValueGeneratedOnAdd();

            entity.HasKey(dw => new { Id = dw.DeckWordId, });

            entity.HasIndex(dw => new { dw.WordId, dw.ReadingIndex })
                  .HasDatabaseName("IX_WordReadingIndex");

            entity.HasIndex(dw => new { dw.DeckId, dw.WordId, dw.ReadingIndex })
                  .IsUnique()
                  .HasDatabaseName("IX_DeckWords_DeckId_WordId_ReadingIndex");

            entity.HasIndex(dw => dw.DeckId)
                  .HasDatabaseName("IX_DeckId");

            entity.HasOne(dw => dw.Deck)
                  .WithMany(d => d.DeckWords)
                  .HasForeignKey(dw => dw.DeckId);
        });

        modelBuilder.Entity<DeckRawText>(entity =>
        {
            entity.HasKey(drt => drt.DeckId);

            entity.HasIndex(dw => dw.DeckId)
                  .HasDatabaseName("IX_DeckRawText_DeckId");

            entity.HasOne(drt => drt.Deck)
                  .WithOne(d => d.RawText)
                  .HasForeignKey<DeckRawText>(drt => drt.DeckId);
        });

        modelBuilder.Entity<DeckStats>(entity =>
        {
            entity.ToTable("DeckStats", "jiten");
            entity.HasKey(ds => ds.DeckId);

            entity.HasOne(ds => ds.Deck)
                  .WithOne(d => d.DeckStats)
                  .HasForeignKey<DeckStats>(ds => ds.DeckId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ds => ds.ComputedAt)
                  .HasDatabaseName("IX_DeckStats_ComputedAt");

            entity.Property(ds => ds.CoverageCurve)
                  .HasMaxLength(100);

            entity.Ignore(ds => ds.ParameterA);
            entity.Ignore(ds => ds.ParameterB);
            entity.Ignore(ds => ds.ParameterC);
            entity.Ignore(ds => ds.RSquared);
            entity.Ignore(ds => ds.RMSE);
            entity.Ignore(ds => ds.TotalUniqueWords);
        });

        modelBuilder.Entity<DeckDifficulty>(entity =>
        {
            entity.ToTable("DeckDifficulty", "jiten");
            entity.HasKey(dd => dd.DeckId);

            entity.Property(dd => dd.Difficulty)
                  .HasPrecision(4, 2)
                  .IsRequired();

            entity.Property(dd => dd.Peak)
                  .HasPrecision(4, 2)
                  .IsRequired();

            entity.Property(dd => dd.DecilesJson)
                  .HasColumnType("jsonb")
                  .IsRequired();

            entity.Property(dd => dd.ProgressionJson)
                  .HasColumnType("jsonb")
                  .IsRequired();

            entity.Property(dd => dd.LastUpdated)
                  .IsRequired();

            entity.HasOne(dd => dd.Deck)
                  .WithOne(d => d.DeckDifficulty)
                  .HasForeignKey<DeckDifficulty>(dd => dd.DeckId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(dd => dd.Deciles);
            entity.Ignore(dd => dd.Progression);
        });

        modelBuilder.Entity<Link>(entity =>
        {
            entity.ToTable("Links", "jiten");
            entity.HasKey(l => l.LinkId);
            entity.Property(l => l.Url).IsRequired();
            entity.Property(l => l.LinkType).IsRequired();
        });

        modelBuilder.Entity<JmDictWord>(entity =>
        {
            entity.ToTable("Words", "jmdict");
            entity.HasKey(e => e.WordId);
            entity.Property(e => e.WordId).ValueGeneratedNever();
            entity.HasMany(e => e.Definitions)
                  .WithOne()
                  .HasForeignKey(d => d.WordId);
            entity.HasMany(e => e.Lookups)
                  .WithOne()
                  .HasForeignKey(l => l.WordId);

            entity.Property(e => e.Readings)
                  .HasColumnType("text[]");

            entity.Property(e => e.ReadingTypes)
                  .HasColumnType("int[]");

            entity.Property(e => e.ObsoleteReadings)
                  .HasColumnType("text[]")
                  .IsRequired(false);

            entity.Property(e => e.PartsOfSpeech)
                  .HasColumnType("text[]");

            entity.Property(e => e.PitchAccents)
                  .HasColumnType("int[]")
                  .IsRequired(false);
            
            entity.Property(e => e.Origin)
                  .HasColumnType("int");
        });

        modelBuilder.Entity<JmDictDefinition>(entity =>
        {
            entity.ToTable("Definitions", "jmdict");
            entity.HasKey(e => e.DefinitionId);
            entity.Property(e => e.DefinitionId).ValueGeneratedOnAdd();
            entity.Property(e => e.WordId).IsRequired();

            entity.Property(e => e.PartsOfSpeech)
                  .HasColumnType("text[]");
            entity.Property(e => e.EnglishMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.DutchMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.FrenchMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.GermanMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.SpanishMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.HungarianMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.RussianMeanings)
                  .HasColumnType("text[]");
            entity.Property(e => e.SlovenianMeanings)
                  .HasColumnType("text[]");
        });

        modelBuilder.Entity<JmDictLookup>(entity =>
        {
            entity.ToTable("Lookups", "jmdict");
            entity.HasKey(e => new { EntrySequenceId = e.WordId, e.LookupKey });
            entity.Property(e => e.WordId).IsRequired();
            entity.Property(e => e.LookupKey).IsRequired();
        });

        modelBuilder.Entity<JmDictWordFrequency>(entity =>
        {
            entity.ToTable("WordFrequencies", "jmdict");
            entity.HasKey(e => e.WordId);
            entity.HasOne<JmDictWord>()
                  .WithMany()
                  .HasForeignKey(f => f.WordId);
        });

        modelBuilder.Entity<Kanji>(entity =>
        {
            entity.ToTable("Kanji", "jmdict");
            entity.HasKey(e => e.Character);
            entity.Property(e => e.Character)
                  .HasColumnType("text")
                  .ValueGeneratedNever()
                  .IsRequired();

            entity.Property(e => e.OnReadings)
                  .HasColumnType("text[]");

            entity.Property(e => e.KunReadings)
                  .HasColumnType("text[]");

            entity.Property(e => e.Meanings)
                  .HasColumnType("text[]");

            entity.Property(e => e.StrokeCount)
                  .IsRequired();

            entity.HasIndex(e => e.FrequencyRank)
                  .HasDatabaseName("IX_Kanji_FrequencyRank");

            entity.HasIndex(e => e.JlptLevel)
                  .HasDatabaseName("IX_Kanji_JlptLevel");

            entity.HasIndex(e => e.StrokeCount)
                  .HasDatabaseName("IX_Kanji_StrokeCount");
        });

        modelBuilder.Entity<WordKanji>(entity =>
        {
            entity.ToTable("WordKanji", "jmdict");
            entity.HasKey(e => new { e.WordId, e.ReadingIndex, e.KanjiCharacter, e.Position });

            entity.Property(e => e.KanjiCharacter)
                  .HasColumnType("text")
                  .IsRequired();

            entity.HasIndex(e => e.KanjiCharacter)
                  .HasDatabaseName("IX_WordKanji_KanjiCharacter");

            entity.HasIndex(e => new { e.WordId, e.ReadingIndex })
                  .HasDatabaseName("IX_WordKanji_WordId_ReadingIndex");

            entity.HasOne(e => e.Word)
                  .WithMany()
                  .HasForeignKey(e => e.WordId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Kanji)
                  .WithMany(k => k.WordKanjis)
                  .HasForeignKey(e => e.KanjiCharacter)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExampleSentence>(entity =>
        {
            entity.ToTable("ExampleSentences", "jiten");
            entity.HasKey(e => e.SentenceId);
            entity.Property(e => e.SentenceId).ValueGeneratedOnAdd();
            entity.Property(e => e.Text).IsRequired();
            
            entity.HasIndex(e => e.DeckId).HasDatabaseName("IX_ExampleSentence_DeckId");
            
            entity.HasOne(e => e.Deck)
                  .WithMany(d => d.ExampleSentences)
                  .HasForeignKey(e => e.DeckId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasMany(e => e.Words)
                  .WithOne(w => w.ExampleSentence)
                  .HasForeignKey(w => w.ExampleSentenceId);
        });
        
        modelBuilder.Entity<ExampleSentenceWord>(entity =>
        {
            entity.ToTable("ExampleSentenceWords", "jiten");
            entity.HasKey(e => new { e.ExampleSentenceId, e.WordId, e.Position });

            entity.HasIndex(dw => new { dw.WordId, dw.ReadingIndex }).HasDatabaseName("IX_ExampleSentenceWord_WordIdReadingIndex");

            entity.HasOne(e => e.Word)
                  .WithMany()
                  .HasForeignKey(e => e.WordId);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags", "jiten");
            entity.HasKey(t => t.TagId);
            entity.Property(t => t.TagId).ValueGeneratedOnAdd();
            entity.Property(t => t.Name).IsRequired().HasMaxLength(50);

            entity.HasIndex(t => t.Name)
                  .IsUnique()
                  .HasDatabaseName("IX_Tags_Name");
        });

        modelBuilder.Entity<DeckGenre>(entity =>
        {
            entity.ToTable("DeckGenres", "jiten");
            entity.HasKey(dg => new { dg.DeckId, dg.Genre });

            entity.HasIndex(dg => dg.Genre)
                  .HasDatabaseName("IX_DeckGenres_Genre");

            entity.HasIndex(dg => dg.DeckId)
                  .HasDatabaseName("IX_DeckGenres_DeckId");

            entity.HasOne(dg => dg.Deck)
                  .WithMany(d => d.DeckGenres)
                  .HasForeignKey(dg => dg.DeckId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeckTag>(entity =>
        {
            entity.ToTable("DeckTags", "jiten");
            entity.HasKey(dt => new { dt.DeckId, dt.TagId });

            entity.HasIndex(dt => dt.TagId)
                  .HasDatabaseName("IX_DeckTags_TagId");

            entity.HasIndex(dt => dt.Percentage)
                  .HasDatabaseName("IX_DeckTags_Percentage");

            entity.HasOne(dt => dt.Deck)
                  .WithMany(d => d.DeckTags)
                  .HasForeignKey(dt => dt.DeckId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(dt => dt.Tag)
                  .WithMany(t => t.DeckTags)
                  .HasForeignKey(dt => dt.TagId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint("CK_DeckTags_Percentage", "\"Percentage\" >= 0 AND \"Percentage\" <= 100"));
        });

        modelBuilder.Entity<ExternalGenreMapping>(entity =>
        {
            entity.ToTable("ExternalGenreMappings", "jiten");
            entity.HasKey(e => e.ExternalGenreMappingId);
            entity.Property(e => e.ExternalGenreMappingId).ValueGeneratedOnAdd();

            entity.Property(e => e.Provider).IsRequired();
            entity.Property(e => e.ExternalGenreName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.JitenGenre).IsRequired();

            entity.HasIndex(e => new { e.Provider, e.ExternalGenreName, e.JitenGenre })
                  .IsUnique()
                  .HasDatabaseName("IX_ExternalGenreMapping_Provider_ExternalName_JitenGenre");

            entity.HasIndex(e => new { e.Provider, e.ExternalGenreName })
                  .HasDatabaseName("IX_ExternalGenreMapping_Provider_ExternalName");
        });

        modelBuilder.Entity<ExternalTagMapping>(entity =>
        {
            entity.ToTable("ExternalTagMappings", "jiten");
            entity.HasKey(e => e.ExternalTagMappingId);
            entity.Property(e => e.ExternalTagMappingId).ValueGeneratedOnAdd();

            entity.Property(e => e.Provider).IsRequired();
            entity.Property(e => e.ExternalTagName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TagId).IsRequired();

            entity.HasIndex(e => new { e.Provider, e.ExternalTagName, e.TagId })
                  .IsUnique()
                  .HasDatabaseName("IX_ExternalTagMapping_Provider_ExternalName_TagId");

            entity.HasIndex(e => new { e.Provider, e.ExternalTagName })
                  .HasDatabaseName("IX_ExternalTagMapping_Provider_ExternalName");

            entity.HasOne(e => e.Tag)
                  .WithMany()
                  .HasForeignKey(e => e.TagId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeckRelationship>(entity =>
        {
            entity.ToTable("DeckRelationships", "jiten");
            entity.HasKey(dr => new { dr.SourceDeckId, dr.TargetDeckId, dr.RelationshipType });

            entity.HasOne(dr => dr.SourceDeck)
                  .WithMany(d => d.RelationshipsAsSource)
                  .HasForeignKey(dr => dr.SourceDeckId)
                  .HasConstraintName("FK_DeckRelationships_Decks_SourceDeckId")
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(dr => dr.TargetDeck)
                  .WithMany(d => d.RelationshipsAsTarget)
                  .HasForeignKey(dr => dr.TargetDeckId)
                  .HasConstraintName("FK_DeckRelationships_Decks_TargetDeckId")
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(dr => dr.SourceDeckId).HasDatabaseName("IX_DeckRelationships_SourceDeckId");
            entity.HasIndex(dr => dr.TargetDeckId).HasDatabaseName("IX_DeckRelationships_TargetDeckId");
        });

        modelBuilder.Entity<WordSet>(entity =>
        {
            entity.ToTable("WordSets", "jiten");
            entity.HasKey(ws => ws.SetId);
            entity.Property(ws => ws.Slug).HasMaxLength(50).IsRequired();
            entity.Property(ws => ws.Name).HasMaxLength(100).IsRequired();
            entity.Property(ws => ws.CreatedAt).IsRequired();
            entity.HasIndex(ws => ws.Slug).IsUnique().HasDatabaseName("IX_WordSet_Slug");
        });

        modelBuilder.Entity<WordSetMember>(entity =>
        {
            entity.ToTable("WordSetMembers", "jiten");
            entity.HasKey(wsm => new { wsm.SetId, wsm.WordId, wsm.ReadingIndex });
            entity.HasOne(wsm => wsm.Set)
                  .WithMany(ws => ws.Members)
                  .HasForeignKey(wsm => wsm.SetId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(wsm => new { wsm.WordId, wsm.ReadingIndex })
                  .HasDatabaseName("IX_WordSetMember_WordId_ReadingIndex");
        });

        base.OnModelCreating(modelBuilder);
    }
}
