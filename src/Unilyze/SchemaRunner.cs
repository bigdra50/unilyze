namespace Unilyze;

internal static class SchemaRunner
{
    public static int Run(string[] args)
    {
        var usageError = ProgramHelpers.ValidateSchemaArgs(args);
        if (usageError != 0)
            return usageError;
        return PrintSchema();
    }

    static int PrintSchema()
    {
        Console.Write(EmbeddedCliText.Schema);
        return 0;
    }
}
