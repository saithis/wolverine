namespace WolverineBugs;

public class AppSettings
{
    public required ConnectionStringConfig ConnectionStrings { get; set; }

    public class ConnectionStringConfig
    {
        public required string RabbitMQ { get; set; }
        public required string Database { get; set; }
    }
}