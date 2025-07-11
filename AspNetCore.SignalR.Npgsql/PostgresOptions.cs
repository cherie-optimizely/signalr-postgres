using System.ComponentModel.DataAnnotations;

namespace AspNetCore.SignalR.Npgsql;
public class PostgresOptions
{
    /// <summary>
    /// The Postgres connection string to use.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// The name of the database schema to use for the underlying PostgresSQL Tables.
    /// </summary>
    public string SchemaName { get; set; } = "SignalR";

    /// <summary>
    /// Function that determines the part of the Postgres table name that identifies the Hub.
    /// It should be assumed that 15 characters of Postgres's 128 character max are not available for use.
    /// By default, uses the Hub's unqualified type name.
    /// </summary>
    public Func<Type, string> TableSlugGenerator { get; set; } = type => type.Name;

    /// <summary>
    /// The PostgreSQL notification channel name to use for message distribution.
    /// </summary>
    public string NotificationChannel { get; set; } = "signalr_notification_channel";

    /// <summary>
    /// Maximum number of retry attempts for the notification listener before giving up.
    /// Default is 10.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 10;

    /// <summary>
    /// Base delay for exponential backoff when retrying failed connections.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Timeout for PostgreSQL operations. If not set, uses the connection string timeout.
    /// </summary>
    public TimeSpan? CommandTimeout { get; set; }
}