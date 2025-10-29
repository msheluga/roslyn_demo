

using Microsoft.Extensions.Configuration;

public class QueryGenerator
{
    public static IConfiguration Configuration { get; set; }

    private static readonly List<string> DefaultQueryMethodAttributes = new List<string>()
    {
        "UsePaging", "UseProjection", "UseFiltering", "UseSorting"
    };
    
    public static async Task GenerateQueryClass()
    {
       
        await WriteFile(GenerateQuery());
    }

    private static async Task WriteFile(string content)
    {
        try
        {
            var DirectoryName = Configuration.GetValue<string>("DirectoryName", "C:/Temp");
            var FileName = Configuration.GetValue<string>("FileName", "Query.cs");
            if (!Directory.Exists(DirectoryName))
            {
                Directory.CreateDirectory(DirectoryName);
            }
            using FileStream fs = new FileStream(Path.Combine(DirectoryName, FileName), FileMode.Create);
            using StreamWriter str = new StreamWriter(fs);
            str.WriteLine(content);
            str.Flush();
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.WriteLine(ex);
        }
    }

    private static string GenerateQuery()
    {
        var builder = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        Configuration = builder.Build();
        throw new NotImplementedException();
    }
}