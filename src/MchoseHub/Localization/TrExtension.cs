using System;
using System.Windows.Markup;

namespace MchoseHub.Localization
{
    /// <summary>
    /// XAML helper: <c>Text="{loc:Tr Some_Key}"</c> resolves the key through
    /// <see cref="Loc"/> while the window is being loaded, so the whole UI follows the
    /// language chosen at startup.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TrExtension : MarkupExtension
    {
        public TrExtension()
        {
        }

        public TrExtension(string key)
        {
            Key = key;
        }

        public string Key { get; set; } = string.Empty;

        public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
    }
}
