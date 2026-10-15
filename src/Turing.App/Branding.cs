namespace Turing.App;

/// <summary>Logo de TECNOTICS incluido en el ejecutable (Assets/).</summary>
internal static class Branding
{
    /// <summary>Icono del chip en el tamaño pedido (el .ico trae de 16 a 256 px).</summary>
    public static Icon AppIcon(Size size)
    {
        using var s = Open("tecnotics.ico");
        return new Icon(s, size);
    }

    /// <summary>Logo completo con el nombre, para la cabecera de la ventana.</summary>
    public static Image Logo()
    {
        using var s = Open("logo.png");
        return new Bitmap(Image.FromStream(s));
    }

    private static Stream Open(string name) =>
        typeof(Branding).Assembly.GetManifestResourceStream($"Turing.App.Assets.{name}")
        ?? throw new InvalidOperationException($"Falta el recurso {name}.");
}
