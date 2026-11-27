using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Dosyaktar.Localization
{
    /// <summary>
    /// XAML usage: Text="{loc:T 'Keşif'}". The Turkish source text is the key and
    /// also the fallback; the binding refreshes when the language changes.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class TExtension : MarkupExtension
    {
        public TExtension() { }

        public TExtension(string key) => Key = key;

        [ConstructorArgument("key")]
        public string Key { get; set; } = "";

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding(nameof(LocalizedEntry.Value))
            {
                Source = Loc.Entry(Key),
                Mode = BindingMode.OneWay,
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
