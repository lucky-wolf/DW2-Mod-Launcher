using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DW2ModLauncher.Avalonia.ViewModels;
using Xunit;

namespace DW2ModLauncher.Tests
{
    public class LocalizedStringsTests
    {
        [Fact]
        public void ChangingTheLanguageRaisesTheNameOfTheDeclaredIndexer()
        {
            // Avalonia 12 re-reads {Binding L[Key]} only when the raised name is a declared property with index parameters.
            var strings = new LocalizedStrings();
            var raised = new List<string>();
            strings.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

            strings.SetLanguage("ja");

            Assert.Contains(raised, name =>
            {
                PropertyInfo property = typeof(LocalizedStrings).GetTypeInfo().GetDeclaredProperty(name);
                return property != null && property.GetIndexParameters().Any();
            });
        }

        [Fact]
        public void TheIndexerFollowsTheLanguage()
        {
            var strings = new LocalizedStrings();
            strings.SetLanguage("ja");

            Assert.Equal("ランチャーの言語を使う", strings["SettingsWidgetLanguageFromLauncher"]);

            strings.SetLanguage("en");
            Assert.Equal("Use the launcher's language", strings["SettingsWidgetLanguageFromLauncher"]);
        }
    }
}
