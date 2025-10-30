

using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
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
        var code = string.Empty;
        var builder = new ConfigurationBuilder()
           .SetBasePath(Directory.GetCurrentDirectory())
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        Configuration = builder.Build();

        var NameSpace = Configuration.GetValue<string>("NameSpace", "TestDomain");

        //Create a new namespace and add usings
        var @namespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(NameSpace)).NormalizeWhitespace();
        @namespace = AddComment(@namespace, "GenerateQuery", "QueryGenerator");
        @namespace = @namespace
            .AddUsings(CreateUsingDirectives());

        var dbName = Configuration.GetValue<string>("DBName");
        var dbConnectionString = Configuration.GetValue<string>("ConnectionString");
        var scaffoldAssemblyDirectory = Configuration.GetValue<string>("DALDebugDirectory");
        var contextNamespace = Configuration.GetValue<string>("DBNameSpace");

        //convert the information to a DBContext 
        using var context = GetDbContextInstance(dbName, dbConnectionString, scaffoldAssemblyDirectory, contextNamespace);

        var entityTypes = context.Model.GetEntityTypes().Select(e => e.ClrType);
        var dbSets = GetDbSetProperties(context.GetType());

        var contextFactoryProperty = SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName($"IDbContextFactory<{context.GetType().Name}>"), $"DbContextFactory_{dbName }")
                                                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
                                                    .AddAccessorListAccessors(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

        //  Create a class
        var classDeclaration = SyntaxFactory.ClassDeclaration(ClassName).AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        //Ensure our class derives from the right base class
        classDeclaration = classDeclaration.AddBaseListTypes(
            SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName($"QueryBase")));

        var constructor = CreateConstructor(context.GetType().Name);

        //Adds the constructor and methods to the class declaration.
        classDeclaration = classDeclaration.AddMembers(constructor, contextFactoryProperty);
        classDeclaration = classDeclaration.AddMembers(CreateMethods(entityTypes, dbSets).ToArray());

        //Closes the class and adds comments to the top.
        classDeclaration = classDeclaration.WithCloseBraceToken(classDeclaration.CloseBraceToken)
                            .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(@"/// <summary>
    /// Provides a means to expose each entity type through an aptly named method.
    /// </summary>" + Environment.NewLine));

        @namespace = @namespace.AddMembers(classDeclaration);
        code = @namespace
                .NormalizeWhitespace()
                .ToFullString();

        return code;
    }

    private static ConstructorDeclarationSyntax CreateConstructor(string dbContextTypeName)
    {
        var constructor = SyntaxFactory.ConstructorDeclaration("Query").WithParameterList(CreateConstructorParameters(dbContextTypeName))
               .WithInitializer(
                   SyntaxFactory.ConstructorInitializer(SyntaxKind.BaseConstructorInitializer)
                   // could be BaseConstructorInitializer or ThisConstructorInitializer
                   .AddArgumentListArguments(
                      SyntaxFactory.Argument(SyntaxFactory.IdentifierName("cachingService")),
                      SyntaxFactory.Argument(SyntaxFactory.IdentifierName("contextAccessor")),
                      SyntaxFactory.Argument(SyntaxFactory.IdentifierName("options")),
                      SyntaxFactory.Argument(SyntaxFactory.IdentifierName("config"))
                   )
               )
               .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
               .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(@"/// <summary>
        /// Initializes a new instance of the <see cref=""Query""/> class.
        /// </summary>
        /// <param name=""factory""></param>
        /// <param name=""cachingService""></param>
        /// <param name=""contextAccessor""></param>    
        /// <param name=""config""></param>" + Environment.NewLine));
        constructor = constructor.AddBodyStatements(GetStatementSyntaxArray($"DbContextFactory_{dbContextTypeName} = factory;"));
        return constructor;
    }

    private static StatementSyntax[] GetStatementSyntaxArray(string methodStatements)
    {
        var statementLines = Regex.Split(methodStatements, @"(?<=[;])");
        var statementArray = new StatementSyntax[statementLines.Length];
        for (int i = 0; i < statementLines.Length; i++)
        {
            statementArray[i] = SyntaxFactory.ParseStatement(statementLines[i]);
        }
        return statementArray;
    }

    private static ParameterListSyntax CreateConstructorParameters(object dbContextTypeName)
    {
        var dbContextParameter = SyntaxFactory.Parameter(default, SyntaxFactory.TokenList(),
                                                       SyntaxFactory.ParseTypeName($"IDbContextFactory<{dbContextTypeName}>"),
                                                       SyntaxFactory.Identifier("factory"), null);
        var portalCachingServiceParameter = SyntaxFactory.Parameter(default, SyntaxFactory.TokenList(),
                                                    SyntaxFactory.ParseTypeName("IAPICachingService"),
                                                    SyntaxFactory.Identifier("cachingService"), null);
        var httpContextAccessorParameter = SyntaxFactory.Parameter(default, SyntaxFactory.TokenList(),
                                                    SyntaxFactory.ParseTypeName("IHttpContextAccessor"),
                                                    SyntaxFactory.Identifier("contextAccessor"), null);        
        var configurationParameter = SyntaxFactory.Parameter(default, SyntaxFactory.TokenList(),
                                                    SyntaxFactory.ParseTypeName("IConfiguration"),
                                                    SyntaxFactory.Identifier("config"), null);
        IEnumerable<ParameterSyntax> parameters = new List<ParameterSyntax>()
            {
                dbContextParameter, portalCachingServiceParameter,
                httpContextAccessorParameter, configurationParameter
            };
        return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
    }

    private static IEnumerable<MethodDeclarationSyntax> CreateMethods(IEnumerable<Type> entityTypes, List<PropertyInfo> dbSets)
    {
        List<MethodDeclarationSyntax> methods = new List<MethodDeclarationSyntax>();
        foreach (var entityType in entityTypes)
        {       
            var dbSetName = dbSets?.FirstOrDefault(d => d.PropertyType.GetGenericArguments()[0].Equals(entityType))?.Name;
            methods.Add(CreateMethodDeclarationForEntity(entityType, dbSetName));            
        }
        return methods;
    }

    private MethodDeclarationSyntax CreateMethodDeclarationForEntity(Type entityType, string dbSetName)
    {
        var methodAttributes = GetAttributeList(MethodAttributes.ToArray());
        var returnType = $"IQueryable<{entityType.Name}>";
        var methodName = $"Get{dbSetName}";

        var parameterList = SyntaxFactory.ParameterList();

        var statementArray = GetStatementSyntaxArray(
                                $@"var context = DbContextFactory_{dbSetName}.CreateDbContext();
return GetData<{entityType.Name}>(context.{dbSetName}).AsNoTracking();");

        var method = SyntaxFactory.MethodDeclaration(
                  attributeLists: methodAttributes,
                  modifiers: SyntaxFactory.TokenList().Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                  returnType: SyntaxFactory.ParseTypeName(returnType),
                  explicitInterfaceSpecifier: null,
                  identifier: SyntaxFactory.Identifier(methodName),
                  typeParameterList: null,
                  parameterList: parameterList, //parameterlist can't be null
                  constraintClauses: SyntaxFactory.List<TypeParameterConstraintClauseSyntax>(),
                  body: null,
                  semicolonToken: default).WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(
                      $@"/// <summary>
        /// Gets the {dbSetName}.
        /// </summary>
        /// <returns>An IQueryable of {entityType.Name}</returns>" + Environment.NewLine));
        return method.AddBodyStatements(statementArray);
    }

    protected static SyntaxList<AttributeListSyntax> GetAttributeList(params string[] attributeNames)
    {
        SyntaxList<AttributeListSyntax> attributes = SyntaxFactory.List<AttributeListSyntax>();
        foreach (var attributeName in attributeNames)
        {
            attributes = attributes.Add(SyntaxFactory.AttributeList(
                                            SyntaxFactory.SingletonSeparatedList(
                                                SyntaxFactory.Attribute(
                                                    SyntaxFactory.IdentifierName(attributeName)))));
        }
        return attributes;
    }


    private static List<PropertyInfo> GetDbSetProperties(Type type)
    {
        var dbSetProperties = new List<PropertyInfo>();
        var properties = type.GetProperties();

        foreach (var property in properties)
        {
            var setType = property.PropertyType;

            var isDbSet = setType.IsGenericType && typeof(DbSet<>).IsAssignableFrom(setType.GetGenericTypeDefinition());

            if (isDbSet)
            {
                dbSetProperties.Add(property);
            }
        }

        return dbSetProperties;
    }

    private static NamespaceDeclarationSyntax AddComment(NamespaceDeclarationSyntax nds, string methodName, string className)
    {
        nds = nds.WithLeadingTrivia(
                 SyntaxFactory.Comment("//This file has been auto-generated by Roslyn from the " + methodName + " method in the " + className + " class"));

        return nds;
    }

    protected static UsingDirectiveSyntax[] CreateUsingDirectives()
    {
        var Usings = GetUsings();

        UsingDirectiveSyntax[] syntaxes = new UsingDirectiveSyntax[Usings.Count];
        for (int i = 0; i < Usings.Count; i++)
        {
            syntaxes[i] = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(Usings[i]));
        }
        return syntaxes;
    }

    private static List<string> GetUsings()
    {
        var usings = new List<string>();
            
        usings = Configuration.GetSection("Usings").Get<List<string>>() ?? new List<string>();
         
        usings.AddRange(
        [
            "AdventureWorksGraphQLDemo.Data",
            "AdventureWorksGraphQLDemo.Models"
        ]);

        return usings;
    }

    public static DbContext GetDbContextInstance(string dbName, string dbConnectionString, string scaffoldAssemblyDirectory, string contextNamespace)
    {
        if (string.IsNullOrWhiteSpace(dbName)) throw new ArgumentException("dbName is required", nameof(dbName));
        if (string.IsNullOrWhiteSpace(dbConnectionString)) throw new ArgumentException("dbConnectionString is required", nameof(dbConnectionString));
        if (string.IsNullOrWhiteSpace(scaffoldAssemblyDirectory)) throw new ArgumentException("scaffoldAssemblyDirectory is required", nameof(scaffoldAssemblyDirectory));
        if (string.IsNullOrWhiteSpace(contextNamespace)) throw new ArgumentException("contextNamespace is required", nameof(contextNamespace));


        var assemblyDirectory = Path.Combine(scaffoldAssemblyDirectory, "Debug", "net8.0");
        var DALAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(assemblyDirectory, "\\AdventureWorksGraphQLDemo.dll"));
        var contextType = DALAssembly.GetTypes().Where(x => x.Namespace == contextNamespace && x.Name == dbName).FirstOrDefault();
        var genericType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(genericType);
        var options = SqlServerDbContextOptionsExtensions.UseSqlServer(optionsBuilder, dbConnectionString).Options;

        var context = (DbContext)Activator.CreateInstance(contextType, new object[] { options });

        return context;
    }
}