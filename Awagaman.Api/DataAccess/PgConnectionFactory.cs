using Npgsql;

namespace Awagaman.Api.DataAccess;

public interface IPgConnectionFactory
{
    NpgsqlConnection Create();
    string ConnectionString { get; }
}

public sealed class PgConnectionFactory : IPgConnectionFactory
{
    public PgConnectionFactory(IConfiguration configuration)
    {
        ConnectionString = configuration.GetConnectionString("AwagamanDb")
            ?? throw new InvalidOperationException("Missing connection string 'AwagamanDb'.");
    }

    public string ConnectionString { get; }

    public NpgsqlConnection Create() => new(ConnectionString);
}
