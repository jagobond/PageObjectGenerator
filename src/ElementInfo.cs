// ElementInfo.cs
using HtmlAgilityPack;

namespace PageObjectGenerator
{
    /// <summary>
    /// Stores information about a discovered HTML element for code generation.
    /// </summary>
    internal class ElementInfo
    {
        public HtmlNode Node { get; }
        public string OriginalSuggestedName { get; }
        public string SanitizedName { get; set; } // PascalCase name for C#
        public string LocatorStrategy { get; set; } // "Id", "Name", "CssSelector", "XPath"
        public string LocatorValue { get; set; }
        public string ElementType { get; } // "Input", "Button", "Link", "Select", "TextArea", "Generic"

        public ElementInfo(HtmlNode node, string suggestedName, string elementType)
        {
            Node = node;
            OriginalSuggestedName = suggestedName ?? string.Empty;
            SanitizedName = string.Empty; // Will be sanitized later
            LocatorStrategy = string.Empty;
            LocatorValue = string.Empty;
            ElementType = elementType;
        }
    }
}