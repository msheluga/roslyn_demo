using QueryGenerator;
// See https://aka.ms/new-console-template for more information
using RoslynGenerator.QueryGenerator;

Console.WriteLine("Generate GraphQL Queries");

var response = Console.ReadLine();

switch (response)
{
    case "Y":
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine("Generating GraphQl Class(es)...");
        await QueryGenerator.GenerateQueryClass();

        break;
    default:
        Console.WriteLine("Exiting Program...");
        break;
};

