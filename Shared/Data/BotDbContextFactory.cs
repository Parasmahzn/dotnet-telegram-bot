using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeroShareBot.Shared.Data;

// Used only by `dotnet ef migrations add/...` at design time, so migration generation doesn't
// require booting the full host (and its ConnectionStrings:Default fail-fast check) or a live
// MySQL connection — building the SQL for a migration needs a ServerVersion, not a real connection.
public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseMySql("Server=localhost;Database=design;Uid=design;Pwd=design;", new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new BotDbContext(options);
    }
}
