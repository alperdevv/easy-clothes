using System.ComponentModel;
using ModelContextProtocol.Server;

namespace EasyClothes.Mcp;

[McpServerPromptType]
public static class Prompts
{
    public const string Instructions = """
        EasyClothes gestiona packs de ropa de GTA V para FiveM. Cada proyecto es un .ecproj con sus archivos en assets/.
        - Empieza con find_projects o get_project para conocer las prendas, su sexo, hueco (jbib, lowr, feet, p_head…) y número en el juego.
        - El número de cada prenda es el que usan los scripts del servidor: no muevas ni quites prendas de un pack ya publicado sin avisar, porque cambian los números de las de detrás. Para huecos futuros usa reserve_numbers.
        - Antes de generar, ejecuta validate; apply_fixes arregla texturas grandes, mipmaps y polígonos con copia de seguridad.
        - Mira el resultado con render_garment antes de dar algo por bueno. Para colocar logos usa render_uv_layout y luego edit_texture con coordenadas UV; prueba con save=false y guarda como variación nueva salvo que el usuario pida sobrescribir.
        - Para una gorra desde cero usa create_cap (modelo propio, no hace falta ningún mod) y update_cap para cambiar la forma o sacar otra combinación de colores; los logos van después con edit_texture.
        - Para una camiseta, polo, camisa o jersey de cuello alto desde cero usa create_shirt (se corta del cuerpo exportado y se anima como la ropa del juego): admite estampados, tela, desgaste, logos y textos por zonas (prints) y varios colores de una vez (extraColors). update_shirt cambia la forma o saca otra textura con otro aspecto.
        - Para un accesorio hecho por el usuario en Blender usa import_model (.obj) y place_model para ajustarlo; export_reference_head le da la cabeza sobre la que modelar.
        - Todas las rutas aceptan formato Windows (D:\...) o WSL (/mnt/d/...). Cada cambio se guarda al momento con copia en .backups y la app, si está abierta, se recarga sola.
        """;

    [McpServerPrompt(Name = "revisar_pack"), Description("Revisa un pack antes de publicarlo: problemas, correcciones automáticas, render de cada prenda y resumen.")]
    public static string ReviewPack([Description("Proyecto .ecproj o su carpeta.")] string project) => $"""
        Revisa el proyecto de EasyClothes {project} antes de publicarlo en FiveM:
        1. get_project para ver las prendas y validate para los problemas.
        2. Aplica apply_fixes a lo que tenga corrección automática y explícame lo que quede.
        3. Haz render_garment de cada prenda (frente y espalda) y fíjate en texturas que no encajan, piezas que atraviesan el cuerpo o tacones sin marcar.
        4. Comprueba tacones en el calzado, pelo bajo los sombreros y modelos de primera persona en capuchas y cascos.
        5. Termina con un resumen corto: lo que has arreglado, lo que necesita mi decisión y si está listo para build_resource.
        """;

    [McpServerPrompt(Name = "importar_pack"), Description("Importa un pack desde una carpeta a un proyecto nuevo o existente, lo revisa y genera el recurso.")]
    public static string ImportPack(
        [Description("Carpeta con el pack descargado o el recurso de FiveM.")] string source,
        [Description("Proyecto .ecproj o carpeta donde importarlo; si no existe, se crea.")] string project) => $"""
        Importa el pack de {source} en el proyecto {project} con EasyClothes:
        1. Si el proyecto no existe, créalo con create_project.
        2. import_garments con la carpeta. Si es un recurso ya generado conservará los números; si no, revisa que sexo y hueco de cada prenda sean correctos y corrígelos con update_garment.
        3. Revisa tacones y sombreros, ejecuta validate y apply_fixes.
        4. Enséñame un render de las prendas principales y pregúntame antes de build_resource.
        """;
}
