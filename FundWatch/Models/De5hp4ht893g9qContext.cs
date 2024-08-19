using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace FundWatch.Models;

public partial class De5hp4ht893g9qContext : DbContext
{
    public De5hp4ht893g9qContext()
    {
    }

    public De5hp4ht893g9qContext(DbContextOptions<De5hp4ht893g9qContext> options)
        : base(options)
    {
    }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_stat_statements");

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
