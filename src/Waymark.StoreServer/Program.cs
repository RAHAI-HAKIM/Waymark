// Waymark.StoreServer — the authoritative process on the store premises.
//
// Phase 0 placeholder. It exists so the solution builds and so there is
// somewhere for the Phase 0.5 walking skeleton to land. It owns no database
// yet and exposes no API yet.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// The POS uses this to decide whether the server is reachable before falling
// back to its Level-2 cache.
app.MapGet("/health", () => Results.Ok(new
{
    service = "Waymark.StoreServer",
    status = "up",
    checkedAt = DateTimeOffset.UtcNow
}));

app.Run();
