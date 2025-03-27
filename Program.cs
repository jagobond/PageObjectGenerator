// Program.cs
using PageObjectGenerator;
using System;
using System.IO;
using System.Threading.Tasks;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("--- Selenium Page Object Generator ---");

        if (args.Length < 2 || args.Length > 3)
        {
            PrintUsage();
            return 1; // Error code
        }

        string url = args[0];
        string outputFilePath = args[1];
        string? className = args.Length == 3 ? args[2] : null;

        // Validate URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            Console.Error.WriteLine($"Invalid URL specified: {url}");
            Console.Error.WriteLine("URL must start with http:// or https://");
            return 1;
        }
        url = uriResult.ToString(); // Use the validated and potentially cleaned URL

        // Validate output path and determine class name
        try
        {
            // Ensure directory exists
            string? directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine($"Created output directory: {directory}");
            }

            // Generate class name from file name if not provided
            if (string.IsNullOrWhiteSpace(className))
            {
                className = Path.GetFileNameWithoutExtension(outputFilePath);
                // Basic sanitization for class name derived from file
                className = System.Text.RegularExpressions.Regex.Replace(className, @"[^a-zA-Z0-9_]", "");
                if (string.IsNullOrWhiteSpace(className) || char.IsDigit(className[0]))
                {
                    className = "GeneratedPage"; // Fallback class name
                }
                // Ensure PascalCase
                 if (className.Length > 0)
                    className = char.ToUpperInvariant(className[0]) + className.Substring(1);

                Console.WriteLine($"Using generated class name: {className}");
            }

             // Further sanitize provided class name
             if (!IsValidCSharpIdentifier(className))
             {
                 Console.Error.WriteLine($"Invalid class name specified or generated: '{className}'. Class names must be valid C# identifiers (and not keywords).");
                 return 1;
             }


            // Ensure file path ends with .cs
            if (!outputFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                outputFilePath += ".cs";
                Console.WriteLine($"Appending .cs extension. Output file: {outputFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing output path '{outputFilePath}': {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Starting generation for URL: {url}");
        Console.WriteLine($"Output class name: {className}");
        Console.WriteLine($"Output file path: {outputFilePath}");
        Console.WriteLine("---------------------------------------");


        var generator = new Generator();
        string generatedCode = await generator.GeneratePageObjectClassAsync(url, className!); // className is guaranteed non-null here after validation

        if (string.IsNullOrEmpty(generatedCode))
        {
            Console.Error.WriteLine("Code generation failed.");
            return 1; // Error code
        }

        try
        {
            await File.WriteAllTextAsync(outputFilePath, generatedCode);
            Console.WriteLine("---------------------------------------");
            Console.WriteLine($"Successfully generated page object class at: {outputFilePath}");
            Console.WriteLine("IMPORTANT: Review the generated code. Locators might need refinement, especially for dynamic pages.");
            return 0; // Success code
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error writing output file '{outputFilePath}': {ex.Message}");
            return 1; // Error code
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  PageObjectGenerator <URL> <OutputFilePath> [ClassName]");
        Console.WriteLine("\nArguments:");
        Console.WriteLine("  <URL>             The full URL of the web page (e.g., https://www.google.com).");
        Console.WriteLine("  <OutputFilePath>  The path where the generated C# file will be saved (e.g., c:\\temp\\LoginPage.cs).");
        Console.WriteLine("                    If the path doesn't end with .cs, it will be appended.");
        Console.WriteLine("  [ClassName]       (Optional) The name of the generated C# class.");
        Console.WriteLine("                    If omitted, it will be derived from the <OutputFilePath> (e.g., LoginPage).");
        Console.WriteLine("\nExample:");
        Console.WriteLine("  dotnet run -- https://example.com ./PageObjects/ExamplePage.cs ExamplePage");
        Console.WriteLine("  (Run from within the project directory)");
    }

     static bool IsValidCSharpIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        // Check first character
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_') return false;
        // Check remaining characters
        for (int i = 1; i < identifier.Length; i++)
        {
            if (!char.IsLetterOrDigit(identifier[i]) && identifier[i] != '_') return false;
        }

        // *** UPDATED LINE: Check against the public keyword list from Generator ***
        // Convert the identifier to lowercase for the check, as the list contains lowercase keywords.
        if (Generator.CSharpKeywords.Contains(identifier.ToLowerInvariant())) return false;

        return true;
    }
}