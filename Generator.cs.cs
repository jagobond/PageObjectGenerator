// Generator.cs
using HtmlAgilityPack;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace PageObjectGenerator
{
    internal class Generator
    {
        // Basic sanitization: Remove invalid chars, PascalCase, handle keywords
        private static readonly Regex InvalidCharsRegex = new Regex(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);
        private static readonly Regex StartWithNumberRegex = new Regex(@"^\d", RegexOptions.Compiled);

        // *** CHANGE THIS LINE FROM private TO public ***
        public static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while", "add", "alias", "ascending", "async", "await",
            "by", "descending", "dynamic", "equals", "from", "get", "global", "group", "into",
            "join", "let", "nameof", "on", "orderby", "partial", "remove", "select", "set",
            "unmanaged", "value", "var", "when", "where", "with", "yield"
        };


        private readonly HttpClient _httpClient;
        private const int DefaultWaitSeconds = 10; // Default explicit wait timeout

        // ... (rest of the Generator.cs class remains the same as before) ...
        // Constructor
        public Generator()
        {
            // Configure HttpClient to handle potential redirects and mimic a browser user agent
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public async Task<string> GeneratePageObjectClassAsync(string url, string className)
        {
            Console.WriteLine($"Attempting to fetch HTML from: {url}");
            string htmlContent;
            try
            {
                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode(); // Throw if not successful
                htmlContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Successfully fetched HTML ({htmlContent.Length} bytes).");
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Error fetching URL: {ex.Message}");
                if (ex.InnerException != null) Console.Error.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                if (ex.StatusCode.HasValue) Console.Error.WriteLine($"Status Code: {ex.StatusCode}");
                Console.Error.WriteLine($"Please check the URL and network connection.");
                return string.Empty; // Indicate failure
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An unexpected error occurred during fetch: {ex.Message}");
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                Console.Error.WriteLine("Fetched HTML content is empty.");
                return string.Empty;
            }

            Console.WriteLine("Parsing HTML content...");
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            Console.WriteLine("Identifying relevant elements...");
            List<ElementInfo> elements = IdentifyElements(htmlDoc);

            Console.WriteLine($"Found {elements.Count} potential elements.");
            if (elements.Count == 0)
            {
                Console.WriteLine("No relevant elements found to generate locators for.");
                // Optionally generate an empty class structure
            }

            Console.WriteLine("Generating C# code...");
            string generatedCode = GenerateCSharpCode(className, elements);

            Console.WriteLine("Code generation complete.");
            return generatedCode;
        }

        private List<ElementInfo> IdentifyElements(HtmlDocument htmlDoc)
        {
            var elements = new List<ElementInfo>();
            var nodes = htmlDoc.DocumentNode.Descendants()
                               .Where(n => IsRelevantElement(n))
                               .ToList();

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodes)
            {
                string elementType = GetElementType(node);
                string suggestedName = GenerateSuggestedName(node);

                var elementInfo = new ElementInfo(node, suggestedName, elementType);

                if (TryGenerateLocator(node, out string? strategy, out string? value))
                {
                    elementInfo.LocatorStrategy = strategy!;
                    elementInfo.LocatorValue = value!;
                    elementInfo.SanitizedName = SanitizeName(suggestedName, elementType, nameCounts);
                    elements.Add(elementInfo);
                }
                else
                {
                    // Optionally log elements for which no good locator could be found
                    Console.WriteLine($"Warning: Could not generate a reliable locator for element: {node.OuterHtml.Substring(0, Math.Min(node.OuterHtml.Length, 100))}...");
                }
            }

            // Secondary pass for uniqueness check if needed, though SanitizeName handles basic cases
            EnsureUniqueNames(elements);

            return elements;
        }

        private bool IsRelevantElement(HtmlNode node)
        {
            // Target common interactive elements
            return node.NodeType == HtmlNodeType.Element &&
                   (node.Name == "input" ||
                    node.Name == "button" ||
                    node.Name == "a" ||
                    node.Name == "select" ||
                    node.Name == "textarea" ||
                    // Maybe divs/spans with specific roles or IDs? Keep it simple for now.
                    (node.Name == "div" && node.GetAttributeValue("role", null) == "button") ||
                    (node.Name == "span" && node.GetAttributeValue("role", null) == "button"));

            // Add more complex rules if needed (e.g., elements with specific data-testid attributes)
        }

        private string GetElementType(HtmlNode node)
        {
            return node.Name switch
            {
                "input" => node.GetAttributeValue("type", "text").ToLowerInvariant() switch
                {
                    "button" or "submit" or "reset" => "Button",
                    "checkbox" => "Checkbox",
                    "radio" => "RadioButton",
                    _ => "Input"
                },
                "button" => "Button",
                "a" => "Link",
                "select" => "Select",
                "textarea" => "TextArea",
                _ => "Generic" // For divs/spans acting as buttons, etc.
            };
        }

        private string GenerateSuggestedName(HtmlNode node)
        {
            // Prioritize ID, Name, then other attributes or text
            string? name = node.GetAttributeValue("id", null);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            name = node.GetAttributeValue("name", null);
            if (!string.IsNullOrWhiteSpace(name)) return name;

            // For specific types, use other attributes
            if (node.Name == "input" || node.Name == "textarea")
            {
                name = node.GetAttributeValue("placeholder", null);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }

            if (node.Name == "input" && (node.GetAttributeValue("type", "") == "submit" || node.GetAttributeValue("type", "") == "button"))
            {
                name = node.GetAttributeValue("value", null);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }

            if (node.Name == "button")
            {
                name = node.InnerText;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }

             if (node.Name == "a")
            {
                name = node.InnerText;
                if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            }

            // Fallback: Use text content (trimmed and shortened)
            name = node.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                // Keep it reasonably short
                if (name.Length > 30) name = name.Substring(0, 30);
                return name;
            }

            // Final fallback: Element type + a generic marker (will be numbered later)
            return GetElementType(node);
        }

        private string SanitizeName(string suggestedName, string elementType, Dictionary<string, int> nameCounts)
        {
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                suggestedName = elementType; // Use element type if suggestion is empty
            }

            // 1. Remove invalid characters and replace spaces/hyphens often used in IDs/names
            string sanitized = suggestedName.Replace('-', '_').Replace(' ', '_');
            sanitized = InvalidCharsRegex.Replace(sanitized, "");

            // 2. Ensure it starts with a letter or underscore
            if (string.IsNullOrEmpty(sanitized) || StartWithNumberRegex.IsMatch(sanitized))
            {
                sanitized = "_" + sanitized;
            }

            // 3. PascalCase (simple version: capitalize first letter, handle underscores)
             if (sanitized.Length > 0)
            {
                 var parts = sanitized.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                 var pascalBuilder = new StringBuilder();
                 foreach (var part in parts)
                 {
                     if (part.Length > 0)
                     {
                         pascalBuilder.Append(char.ToUpperInvariant(part[0]));
                         if (part.Length > 1)
                         {
                             pascalBuilder.Append(part.Substring(1).ToLowerInvariant());
                         }
                     }
                 }
                 sanitized = pascalBuilder.ToString();
             }


            // If empty after sanitization, use element type
             if (string.IsNullOrEmpty(sanitized))
             {
                 sanitized = elementType;
             }

            // 4. Handle C# Keywords - convert sanitized name to lower to check against the keyword list
            if (CSharpKeywords.Contains(sanitized.ToLowerInvariant()))
            {
                sanitized += "Element"; // Append suffix
            }

             // 5. Ensure uniqueness (simple counter)
             string baseName = sanitized;
             int count = nameCounts.TryGetValue(baseName, out int currentCount) ? currentCount + 1 : 1;
             nameCounts[baseName] = count;

             // Append count only if it's not the first instance or if the base name was generic
             if (count > 1 || suggestedName == elementType)
             {
                 sanitized = $"{baseName}{count}";
             }
             else if (string.IsNullOrWhiteSpace(sanitized)) // Absolute fallback
             {
                 sanitized = $"{elementType}{count}";
             }


            return sanitized;
        }

        private void EnsureUniqueNames(List<ElementInfo> elements)
        {
             var finalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
             var duplicates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

             foreach(var element in elements)
             {
                 string nameToCheck = element.SanitizedName;
                 if (!finalNames.Add(nameToCheck))
                 {
                    duplicates[nameToCheck] = duplicates.TryGetValue(nameToCheck, out int count) ? count + 1 : 2; // Start numbering from 2 for duplicates
                 }
             }

            // Rename duplicates
             foreach(var element in elements.Where(e => duplicates.ContainsKey(e.SanitizedName)).Reverse()) // Reverse to rename later items first
             {
                int count = duplicates[element.SanitizedName];
                element.SanitizedName = $"{element.SanitizedName}{count}";
                duplicates[element.SanitizedName] = count - 1; // Decrement count for next potential duplicate with same base name
             }
        }


        private bool TryGenerateLocator(HtmlNode node, out string? strategy, out string? value)
        {
            strategy = null;
            value = null;

            // Priority 1: ID
            string? id = node.GetAttributeValue("id", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                // Basic check for potentially dynamic IDs (could be more sophisticated)
                if (!Regex.IsMatch(id, @"\d{4,}") && !id.Contains("guid", StringComparison.OrdinalIgnoreCase)) // Avoid long numbers or guids
                {
                    strategy = "Id";
                    value = id;
                    return true;
                }
            }

            // Priority 2: Name
            string? name = node.GetAttributeValue("name", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                strategy = "Name";
                value = name;
                return true;
            }

            // Priority 3: Specific data-* attribute (e.g., data-testid)
            string? testId = node.GetAttributeValue("data-testid", null)?.Trim();
             if (!string.IsNullOrWhiteSpace(testId))
             {
                 strategy = "CssSelector";
                 value = $"[data-testid='{testId}']"; // Ensure quotes are handled if value has them
                 return true;
             }
             // Add more data-* attributes if needed


            // Priority 4: CSS Selector (more robust combinations)
             string? css = GenerateCssSelector(node);
             if (!string.IsNullOrWhiteSpace(css))
             {
                 // Basic uniqueness check placeholder - Real check needs DOM execution
                 // For now, assume generated CSS is potentially useful
                 strategy = "CssSelector";
                 value = css;
                 return true;
             }


            // Priority 5: XPath (as fallback)
             string? xpath = GenerateXPath(node);
            if (!string.IsNullOrWhiteSpace(xpath))
            {
                strategy = "XPath";
                value = xpath;
                return true;
            }


            return false; // No suitable locator found
        }

        private string? GenerateCssSelector(HtmlNode node)
        {
            // Try combining tag name with attributes
            var selector = new StringBuilder(node.Name);
            bool addedAttribute = false;

            // Prefer type for inputs
            string? type = node.GetAttributeValue("type", null)?.Trim();
             if (!string.IsNullOrWhiteSpace(type) && node.Name == "input")
             {
                 selector.Append($"[type='{type}']");
                 addedAttribute = true;
             }

            // Add class names if they exist and look reasonable
            string? classes = node.GetAttributeValue("class", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(classes))
            {
                // Avoid overly generic or dynamic-looking classes
                var classList = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                      .Where(c => c.Length > 2 && !Regex.IsMatch(c, @"\d{3,}") && !c.Contains("active") && !c.Contains("selected"));
                foreach (var cls in classList)
                {
                    selector.Append($".{cls}");
                    addedAttribute = true; // Consider class an attribute for uniqueness
                }
            }

            // Add other attributes like placeholder, title, role, value (for buttons)
            string? placeholder = node.GetAttributeValue("placeholder", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(placeholder)) { selector.Append($"[placeholder='{EscapeCssSelectorValue(placeholder)}']"); addedAttribute = true;}

            string? title = node.GetAttributeValue("title", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(title)) { selector.Append($"[title='{EscapeCssSelectorValue(title)}']"); addedAttribute = true;}

            string? role = node.GetAttributeValue("role", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(role)) { selector.Append($"[role='{role}']"); addedAttribute = true;}

            string? valueAttr = node.GetAttributeValue("value", null)?.Trim();
            if (node.Name == "button" || (node.Name == "input" && (type == "submit" || type == "button")))
            {
                 if (!string.IsNullOrWhiteSpace(valueAttr)) { selector.Append($"[value='{EscapeCssSelectorValue(valueAttr)}']"); addedAttribute = true;}
            }


            // If we only have the tag name, it's probably not specific enough
            return addedAttribute ? selector.ToString() : null;
        }

         private string EscapeCssSelectorValue(string value)
        {
            // Basic escape for quotes within the value
            return value.Replace("'", "\\'").Replace("\"", "\\\"");
        }


        private string? GenerateXPath(HtmlNode node)
        {
            // Prefer relative XPath with attributes
            var parts = new List<string>();
            string? id = node.GetAttributeValue("id", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(id)) parts.Add($"@id='{id}'");

            string? name = node.GetAttributeValue("name", null)?.Trim();
            if (!string.IsNullOrWhiteSpace(name)) parts.Add($"@name='{name}'");

            string? type = node.GetAttributeValue("type", null)?.Trim();
             if (node.Name == "input" && !string.IsNullOrWhiteSpace(type)) parts.Add($"@type='{type}'");

            string? text = node.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text) && (node.Name == "button" || node.Name == "a" || node.GetAttributeValue("role", "")=="button") )
            {
                 // Use contains for robustness against extra whitespace/child elements
                 // Normalize space helps with complex whitespace
                 parts.Add($"normalize-space(.)='{EscapeXPathValue(text)}'");
                 // Alternative: contains(., 'text') - less precise
            }

            if (parts.Count > 0)
            {
                return $"//{node.Name}[{string.Join(" and ", parts)}]";
            }

             // Very basic absolute XPath as last resort (highly fragile!) - commented out by default
             // return node.XPath;

            return null; // Cannot generate a reasonable XPath
        }

        private string EscapeXPathValue(string value)
        {
            if (value.Contains("'") && value.Contains("\""))
            {
                // If the value contains both single and double quotes, use concat()
                var parts = value.Split('\'');
                return $"concat('{string.Join("', \"'\", '", parts)}', '')";
            }
            if (value.Contains("'"))
            {
                return $"\"{value}\""; // Use double quotes if value has single quotes
            }
            return $"'{value}'"; // Default to single quotes
        }


        private string GenerateCSharpCode(string className, List<ElementInfo> elements)
        {
            var sb = new StringBuilder();

            // Using statements
            sb.AppendLine("// Generated by PageObjectGenerator");
            sb.AppendLine($"// Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            sb.AppendLine("using OpenQA.Selenium;");
            sb.AppendLine("using OpenQA.Selenium.Support.UI;");
            sb.AppendLine("using SeleniumExtras.WaitHelpers; // Recommended for ExpectedConditions");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading; // For Thread.Sleep if absolutely needed (try to avoid)");
            sb.AppendLine();

            // Namespace (optional, adjust as needed)
            sb.AppendLine($"namespace YourProject.PageObjects");
            sb.AppendLine("{");

            // Class definition
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Represents the {className} page.");
            sb.AppendLine($"    /// NOTE: This is auto-generated code. Review and refine locators and methods.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");

            // Fields
            sb.AppendLine("        private readonly IWebDriver _driver;");
            sb.AppendLine("        private readonly WebDriverWait _wait;");
            sb.AppendLine($"        private readonly TimeSpan _defaultWaitTimeout = TimeSpan.FromSeconds({DefaultWaitSeconds});");
            sb.AppendLine();

            // Constructor
            sb.AppendLine($"        public {className}(IWebDriver driver)");
            sb.AppendLine("        {");
            sb.AppendLine("            _driver = driver ?? throw new ArgumentNullException(nameof(driver));");
            sb.AppendLine("            _wait = new WebDriverWait(_driver, _defaultWaitTimeout);");
            sb.AppendLine("            // You might want to wait for a specific element indicating the page is fully loaded");
            sb.AppendLine("            // _wait.Until(ExpectedConditions.ElementIsVisible(By.Id(\"some-stable-element-id\")));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Locators
            sb.AppendLine("        // --- Locators ---");
            foreach (var element in elements)
            {
                sb.AppendLine($"        private static readonly By _{ToLowerCamelCase(element.SanitizedName)}Locator = By.{element.LocatorStrategy}(\"{EscapeCSharpString(element.LocatorValue)}\");");
            }
            sb.AppendLine();


            // Interaction Methods
            sb.AppendLine("        // --- Interaction Methods ---");
            foreach (var element in elements)
            {
                string methodNameBase = element.SanitizedName;
                string locatorFieldName = $"_{ToLowerCamelCase(element.SanitizedName)}Locator";

                // Find Element Helper (optional, reduces repetition)
                 sb.AppendLine($"        private IWebElement Find{methodNameBase}Element()");
                 sb.AppendLine( "        {");
                 sb.AppendLine($"            // Wait for the element to be present in the DOM");
                 sb.AppendLine($"            _wait.Until(ExpectedConditions.ElementExists({locatorFieldName}));");
                 sb.AppendLine($"            // You might also wait for visibility depending on the interaction:");
                 sb.AppendLine($"            // _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                 sb.AppendLine($"            return _driver.FindElement({locatorFieldName});");
                 sb.AppendLine( "        }");
                 sb.AppendLine();


                // Click Method
                if (element.ElementType == "Button" || element.ElementType == "Link" || element.ElementType == "Checkbox" || element.ElementType == "RadioButton" || element.ElementType == "Generic")
                {
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Clicks the {element.OriginalSuggestedName} {element.ElementType}.");
                    sb.AppendLine($"        /// Waits for the element to be clickable.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        public void Click{methodNameBase}()");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            Console.WriteLine(\"Clicking {methodNameBase}...\");"); // Basic logging
                    sb.AppendLine("            try");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                 var element = _wait.Until(ExpectedConditions.ElementToBeClickable({locatorFieldName}));");
                    sb.AppendLine($"                 element.Click();");
                     sb.AppendLine("                 // Consider adding a small delay or wait for expected outcome if needed");
                     sb.AppendLine("                 // Example: WaitForAjaxOrPageLoad();");
                    sb.AppendLine("            }");
                    sb.AppendLine("            catch (Exception ex)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Console.Error.WriteLine($\"Error clicking {methodNameBase}: {{ex.Message}}\");");
                    sb.AppendLine("                // Rethrow, log, or handle as appropriate for your framework");
                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                // Enter Text Method
                if (element.ElementType == "Input" || element.ElementType == "TextArea")
                {
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Enters text into the {element.OriginalSuggestedName} field.");
                    sb.AppendLine($"        /// Waits for the element to be visible, clears it, then sends keys.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <param name=\"text\">The text to enter.</param>");
                    sb.AppendLine($"        public void Enter{methodNameBase}Text(string text)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            Console.WriteLine($\"Entering text '{{text}}' into {methodNameBase}...\");");
                    sb.AppendLine("            try");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                 var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                    sb.AppendLine("                 element.Clear();");
                    sb.AppendLine("                 element.SendKeys(text);");
                     sb.AppendLine("                 // Consider adding a wait if SendKeys triggers async actions");
                    sb.AppendLine("            }");
                     sb.AppendLine("            catch (Exception ex)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Console.Error.WriteLine($\"Error entering text into {methodNameBase}: {{ex.Message}}\");");
                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                // Get Text/Value Method
                if (element.ElementType == "Input" || element.ElementType == "TextArea")
                {
                     sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Gets the current value from the {element.OriginalSuggestedName} field.");
                    sb.AppendLine($"        /// Waits for the element to be visible.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <returns>The text value of the element.</returns>");
                    sb.AppendLine($"        public string Get{methodNameBase}Value()");
                    sb.AppendLine("        {");
                     sb.AppendLine($"            var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                     sb.AppendLine($"            return element.GetAttribute(\"value\");");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }
                else if (element.ElementType != "Select") // Get InnerText for most others (buttons, links, generics)
                {
                     sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Gets the text content of the {element.OriginalSuggestedName} {element.ElementType}.");
                    sb.AppendLine($"        /// Waits for the element to be visible.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <returns>The inner text of the element.</returns>");
                    sb.AppendLine($"        public string Get{methodNameBase}Text()");
                    sb.AppendLine("        {");
                     sb.AppendLine($"            var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                     sb.AppendLine($"            return element.Text;");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }


                // Is Displayed Method
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// Checks if the {element.OriginalSuggestedName} {element.ElementType} is displayed.");
                sb.AppendLine($"        /// Uses a short wait for presence before checking visibility.");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        /// <returns>True if displayed, false otherwise.</returns>");
                 sb.AppendLine($"        public bool Is{methodNameBase}Displayed()");
                sb.AppendLine("        {");
                 sb.AppendLine("            try");
                 sb.AppendLine("            {");
                 sb.AppendLine($"                // First, wait briefly for the element to exist in the DOM");
                 sb.AppendLine($"                var shortWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(2));");
                 sb.AppendLine($"                shortWait.Until(ExpectedConditions.ElementExists({locatorFieldName}));");
                 sb.AppendLine($"                // Then check if it's currently displayed");
                 sb.AppendLine($"                return _driver.FindElement({locatorFieldName}).Displayed;");
                 sb.AppendLine("            }");
                 sb.AppendLine("            catch (NoSuchElementException)");
                 sb.AppendLine("            {");
                 sb.AppendLine("                return false; // Not present");
                 sb.AppendLine("            }");
                 sb.AppendLine("            catch (WebDriverTimeoutException)");
                 sb.AppendLine("            {");
                 sb.AppendLine("                return false; // Not present within the short wait");
                 sb.AppendLine("            }");
                 sb.AppendLine("             catch (StaleElementReferenceException)");
                 sb.AppendLine("            {");
                 sb.AppendLine("                 // Element was found but became stale, try finding again briefly");
                 sb.AppendLine("                 try { return _driver.FindElement({locatorFieldName}).Displayed; }");
                 sb.AppendLine("                 catch { return false; } // Still stale or gone");
                 sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();


                // Select Methods (for <select> elements)
                if (element.ElementType == "Select")
                {
                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Selects an option from the {element.OriginalSuggestedName} dropdown by its visible text.");
                    sb.AppendLine($"        /// Waits for the select element to be visible.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <param name=\"text\">The visible text of the option to select.</param>");
                    sb.AppendLine($"        public void Select{methodNameBase}ByText(string text)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            Console.WriteLine($\"Selecting '{{text}}' in {methodNameBase}...\");");
                    sb.AppendLine("            try");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                 var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                    sb.AppendLine($"                 var selectElement = new SelectElement(element);");
                    sb.AppendLine($"                 selectElement.SelectByText(text);");
                    sb.AppendLine("            }");
                     sb.AppendLine("            catch (Exception ex)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Console.Error.WriteLine($\"Error selecting by text in {methodNameBase}: {{ex.Message}}\");");
                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                    sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Selects an option from the {element.OriginalSuggestedName} dropdown by its value attribute.");
                    sb.AppendLine($"        /// Waits for the select element to be visible.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <param name=\"value\">The value attribute of the option to select.</param>");
                    sb.AppendLine($"        public void Select{methodNameBase}ByValue(string value)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            Console.WriteLine($\"Selecting option with value '{{value}}' in {methodNameBase}...\");");
                    sb.AppendLine("            try");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                 var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                    sb.AppendLine($"                 var selectElement = new SelectElement(element);");
                    sb.AppendLine($"                 selectElement.SelectByValue(value);");
                    sb.AppendLine("            }");
                     sb.AppendLine("            catch (Exception ex)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Console.Error.WriteLine($\"Error selecting by value in {methodNameBase}: {{ex.Message}}\");");
                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                    sb.AppendLine("        }");
                    sb.AppendLine();

                     sb.AppendLine($"        /// <summary>");
                    sb.AppendLine($"        /// Gets the selected option's text from the {element.OriginalSuggestedName} dropdown.");
                    sb.AppendLine($"        /// Waits for the select element to be visible.");
                    sb.AppendLine($"        /// </summary>");
                    sb.AppendLine($"        /// <returns>The text of the currently selected option.</returns>");
                    sb.AppendLine($"        public string GetSelected{methodNameBase}Text()");
                    sb.AppendLine("        {");
                    sb.AppendLine($"             var element = _wait.Until(ExpectedConditions.ElementIsVisible({locatorFieldName}));");
                    sb.AppendLine($"             var selectElement = new SelectElement(element);");
                    sb.AppendLine($"             return selectElement.SelectedOption.Text;");
                    sb.AppendLine("        }");
                    sb.AppendLine();
                }

                 sb.AppendLine($"        // --- END {methodNameBase} ---");
                 sb.AppendLine();

            }

             // Helper method for waiting (example)
             sb.AppendLine( "        // --- Helper Methods ---");
             sb.AppendLine( "        // Consider adding helper methods for common wait conditions");
             sb.AppendLine( "        // Example: Wait for AJAX calls to complete, wait for page transitions");
             sb.AppendLine( "        /*");
             sb.AppendLine( "        private void WaitForAjaxOrPageLoad(int timeoutSeconds = 15)");
             sb.AppendLine( "        {");
             sb.AppendLine( "            // This is a placeholder. Implementation depends heavily on the ");
             sb.AppendLine( "            // specific application being tested (e.g., check jQuery.active == 0,");
             sb.AppendLine( "            // wait for a loading spinner to disappear, etc.)");
             sb.AppendLine( "            try");
             sb.AppendLine( "            {");
             sb.AppendLine( "                 var jsExecutor = (IJavaScriptExecutor)_driver;");
             sb.AppendLine( "                 var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds));");
             sb.AppendLine( "                 wait.Until(driver => jsExecutor.ExecuteScript(\"return document.readyState\").Equals(\"complete\"));");
             sb.AppendLine( "                 // Add more specific checks if needed, e.g., for jQuery:");
             sb.AppendLine( "                 // wait.Until(driver => (bool)jsExecutor.ExecuteScript(\"return (typeof jQuery !== 'undefined') && (jQuery.active === 0)\"));");
             sb.AppendLine( "            }");
             sb.AppendLine( "             catch (Exception ex)");
             sb.AppendLine( "            {");
             sb.AppendLine( "                Console.Error.WriteLine($\"Error during WaitForAjaxOrPageLoad: {ex.Message}\");");
             sb.AppendLine( "                // Decide if this should throw or just log");
             sb.AppendLine( "            }");
             sb.AppendLine( "        }");
             sb.AppendLine( "        */");
             sb.AppendLine();


            // Class end
            sb.AppendLine("    }");
            sb.AppendLine("}"); // Namespace end

            return sb.ToString();
        }

        private string ToLowerCamelCase(string pascalCase)
        {
            if (string.IsNullOrEmpty(pascalCase)) return pascalCase;
            if (pascalCase.Length == 1) return pascalCase.ToLowerInvariant();
            return char.ToLowerInvariant(pascalCase[0]) + pascalCase.Substring(1);
        }

        private string EscapeCSharpString(string value)
        {
            // Simple escape for double quotes. More complex escaping might be needed for other chars.
            return value.Replace("\"", "\\\"");
        }
    }
}