using System.Xml.Linq;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Rage.Meta;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public sealed class ResourceExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-export-").FullName;
    private string Output => Path.Combine(_dir, "out", "ropa");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Export_renames_assets_in_game_order_and_writes_the_metadata()
    {
        var project = Project(
            Item("zeta", PedGender.Male, ComponentSlot.Top, textures: 2),
            Item("alfa", PedGender.Male, ComponentSlot.Top, textures: 1),
            Item("gorra", PedGender.Male, prop: PropSlot.Hat, textures: 3),
            Item("falda", PedGender.Female, ComponentSlot.Legs, textures: 1));

        var result = ResourceExporter.Export(project, _dir, Output);

        var male = Path.Combine(Output, "stream", "mp_m_freemode_01");
        Assert.Equal("alfa.ydd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_000_u.ydd")));
        Assert.Equal("zeta.ydd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_001_u.ydd")));
        Assert.Equal("zeta_b.ytd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_diff_001_b_uni.ytd")));
        Assert.True(File.Exists(Path.Combine(male, "mp_m_freemode_01_p_ropa^p_head_000.ydd")));
        Assert.True(File.Exists(Path.Combine(male, "mp_m_freemode_01_p_ropa^p_head_diff_000_c.ytd")));
        Assert.True(File.Exists(Path.Combine(Output, "stream", "mp_f_freemode_01", "mp_f_freemode_01_ropa^lowr_diff_000_a_uni.ytd")));

        var variation = PedVariation.Read(ResourceFile.Read(Path.Combine(Output, "stream", "mp_m_freemode_01_ropa.ymt")));
        Assert.Equal([1, 2], variation.Components[ComponentSlot.Top].Select(d => d.Textures));
        Assert.Equal([3], variation.Props[PropSlot.Hat].Select(d => d.Textures));
        Assert.Equal(Jenkins.Hash("ropa"), variation.DlcNameHash);

        var shopPath = Path.Combine(Output, "mp_f_freemode_01_ropa.meta");
        Assert.Equal((byte)'<', File.ReadAllBytes(shopPath)[0]);
        var shop = XDocument.Load(shopPath).Root!;
        Assert.Equal("mp_f_freemode_01_ropa", shop.Element("fullDlcName")?.Value);
        Assert.Equal("SCR_CHAR_MULTIPLAYER_F", shop.Element("eCharacter")?.Value);
        var manifest = File.ReadAllText(Path.Combine(Output, "fxmanifest.lua"));
        Assert.Contains("data_file 'SHOP_PED_APPAREL_META_FILE' 'mp_m_freemode_01_ropa.meta'", manifest);
        Assert.Contains("data_file 'SHOP_PED_APPAREL_META_FILE' 'mp_f_freemode_01_ropa.meta'", manifest);
        Assert.Equal(4, result.Drawables);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Output)!, ".*"));
    }

    [Fact]
    public void First_person_models_cloth_physics_heels_and_hat_hair_reach_the_game_files()
    {
        var hoodie = Item("hoodie", PedGender.Female, ComponentSlot.Top);
        hoodie.FirstPersonModelPath = Extra("hoodie_fp.ydd");
        var skirt = Item("skirt", PedGender.Female, ComponentSlot.Legs);
        skirt.ClothPhysicsPath = Extra("skirt.yld");
        var flats = Item("flats", PedGender.Female, ComponentSlot.Shoes);
        var heels = Item("heels", PedGender.Female, ComponentSlot.Shoes);
        heels.Flags.HeelHeight = 1.3f;
        var beanie = Item("beanie", PedGender.Female, prop: PropSlot.Hat);
        beanie.Flags.HatHair = HatHair.Keep;
        var cap = Item("cap", PedGender.Female, prop: PropSlot.Hat);
        cap.Flags.HatHair = HatHair.Remove;

        var result = ResourceExporter.Export(Project(hoodie, skirt, flats, heels, beanie, cap), _dir, Output);

        Assert.Empty(result.Warnings);
        var female = Path.Combine(Output, "stream", "mp_f_freemode_01");
        Assert.Equal("hoodie_fp.ydd", File.ReadAllText(Path.Combine(female, "mp_f_freemode_01_ropa^jbib_000_u_1.ydd")));
        Assert.Equal("skirt.yld", File.ReadAllText(Path.Combine(female, "mp_f_freemode_01_ropa^lowr_000_u.yld")));

        var variation = PedVariation.Read(ResourceFile.Read(Path.Combine(Output, "stream", "mp_f_freemode_01_ropa.ymt")));
        Assert.Equal(1, variation.Components[ComponentSlot.Top][0].Alternates);
        Assert.True(variation.Components[ComponentSlot.Legs][0].Cloth);
        Assert.Equal([0f, 1.3f], variation.Components[ComponentSlot.Shoes].Select(d => d.HeelHeight));
        Assert.Equal([0f, -1f], variation.Props[PropSlot.Hat].Select(d => d.HairScale));

        var creature = Rbf.Read(File.ReadAllBytes(Path.Combine(Output, "stream", "mp_creaturemetadata_f_ropa.ymt")));
        var shoe = Assert.Single(creature.Element("pedCompExpressions")!.Elements("Item"));
        Assert.Equal(("1", "4", "28462"), (shoe.Element("pedCompVarIndex")?.Attribute("value")?.Value, shoe.Element("pedCompExpressionIndex")?.Attribute("value")?.Value, shoe.Element("ids")?.Value));
        Assert.Equal(["-1", "1"], creature.Element("pedPropExpressions")!.Elements("Item").Select(i => i.Element("pedPropVarIndex")?.Attribute("value")?.Value));

        var shop = XDocument.Load(Path.Combine(Output, "mp_f_freemode_01_ropa.meta")).Root!;
        Assert.Equal("mp_creaturemetadata_f_ropa", shop.Element("creatureMetaData")?.Value);
        var alternate = Assert.Single(XDocument.Load(Path.Combine(Output, "first_person_alternates_ropa.meta")).Root!.Element("alternates")!.Elements("Item"));
        Assert.Equal("mp_f_freemode_01_ropa/jbib_000_u", alternate.Element("assetName")?.Value);
        Assert.Contains("data_file 'PED_FIRST_PERSON_ALTERNATE_DATA' 'first_person_alternates_ropa.meta'", File.ReadAllText(Path.Combine(Output, "fxmanifest.lua")));
    }

    [Fact]
    public void Shop_entries_list_every_garment_and_texture_only_when_asked()
    {
        var project = Project(
            Item("chaqueta", PedGender.Male, ComponentSlot.Top, textures: 2),
            Item("gorra", PedGender.Male, prop: PropSlot.Hat, textures: 1));
        DrawableOrder.Reserve(project, PedGender.Male, ComponentSlot.Top, null);

        ResourceExporter.Export(project, _dir, Output);
        Assert.Empty(XDocument.Load(Path.Combine(Output, "mp_m_freemode_01_ropa.meta")).Root!.Element("pedComponents")!.Elements());

        project.ShopEntries = true;
        ResourceExporter.Export(project, _dir, Output);

        var shop = XDocument.Load(Path.Combine(Output, "mp_m_freemode_01_ropa.meta")).Root!;
        var tops = shop.Element("pedComponents")!.Elements("Item").ToList();
        Assert.Equal(["CLO_ROPA_JBIB_0_0_M", "CLO_ROPA_JBIB_0_1_M"], tops.Select(i => i.Element("uniqueNameHash")?.Value));
        Assert.Equal(["PV_COMP_JBIB", "1"], [tops[1].Element("eCompType")!.Value, tops[1].Element("textureIndex")!.Attribute("value")!.Value]);
        var hat = Assert.Single(shop.Element("pedProps")!.Elements("Item"));
        Assert.Equal(("CLO_ROPA_PHEAD_0_0_M", "ANCHOR_HEAD"), (hat.Element("uniqueNameHash")?.Value, hat.Element("eAnchorPoint")?.Value));
    }

    [Fact]
    public void Plain_clothes_need_no_creature_metadata()
    {
        var hat = Item("hat", PedGender.Male, prop: PropSlot.Hat);
        hat.Flags.HatHair = HatHair.Keep;

        ResourceExporter.Export(Project(hat, Item("shoe", PedGender.Male, ComponentSlot.Shoes)), _dir, Output);

        Assert.Empty(Directory.GetFiles(Path.Combine(Output, "stream"), "mp_creaturemetadata_*"));
        Assert.Null(XDocument.Load(Path.Combine(Output, "mp_m_freemode_01_ropa.meta")).Root!.Element("creatureMetaData"));
        Assert.False(File.Exists(Path.Combine(Output, "first_person_alternates_ropa.meta")));
    }

    [Fact]
    public void An_alternate_used_only_by_switches_is_streamed_but_not_declared_for_first_person()
    {
        var mask = Item("mask", PedGender.Male, ComponentSlot.Mask);
        mask.FirstPersonModelPath = Extra("mask_alt.ydd");
        mask.Flags.AlternateInFirstPerson = false;

        ResourceExporter.Export(Project(mask), _dir, Output);

        Assert.True(File.Exists(Path.Combine(Output, "stream", "mp_m_freemode_01", "mp_m_freemode_01_ropa^berd_000_u_1.ydd")));
        Assert.Equal(1, PedVariation.Read(ResourceFile.Read(Path.Combine(Output, "stream", "mp_m_freemode_01_ropa.ymt"))).Components[ComponentSlot.Mask][0].Alternates);
        Assert.False(File.Exists(Path.Combine(Output, "first_person_alternates_ropa.meta")));
    }

    [Fact]
    public void Export_refuses_projects_with_errors()
    {
        var project = Project(Item("rota", PedGender.Male, ComponentSlot.Top, textures: 1));
        project.Drawables[0].Textures.Add(new TextureVariation { TexturePath = "no-existe.ytd" });

        var error = Assert.Throws<ExportException>(() => ResourceExporter.Export(project, _dir, Output));

        Assert.Contains(error.Errors, e => e.Contains("no-existe.ytd"));
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void Export_replaces_its_own_output_but_not_foreign_folders()
    {
        var project = Project(Item("a", PedGender.Male, ComponentSlot.Top, textures: 1));
        ResourceExporter.Export(project, _dir, Output);
        var stale = Path.Combine(Output, "stream", "viejo.ydd");
        File.WriteAllText(stale, "");

        ResourceExporter.Export(project, _dir, Output);
        Assert.False(File.Exists(stale));

        var foreign = Directory.CreateDirectory(Path.Combine(_dir, "otro")).FullName;
        File.WriteAllText(Path.Combine(foreign, "fxmanifest.lua"), "fx_version 'cerulean'");
        Assert.Throws<IOException>(() => ResourceExporter.Export(project, _dir, foreign));
        Assert.True(File.Exists(Path.Combine(foreign, "fxmanifest.lua")));
    }

    [Fact]
    public void Tattoos_become_texture_dictionaries_and_an_overlay_collection()
    {
        var png = Path.Combine(_dir, "dragón.png");
        using (var file = File.Create(png))
            PngWriter.Write(new RgbaImage(300, 200, Enumerable.Repeat((byte)200, 300 * 200 * 4).ToArray()), file);
        var project = Project();
        var imported = TattooImporter.Import(project, _dir, [png, Path.Combine(_dir, "notas.txt")], TattooZone.LeftArm);
        imported.Added[0].Genders = [PedGender.Female];
        imported.Added[0].UvX = 0.25f;

        var result = ResourceExporter.Export(project, _dir, Output);

        Assert.Single(imported.Warnings);
        Assert.Equal(1, result.Tattoos);
        var ytd = ResourceFile.Read(Path.Combine(Output, "stream", "tattoos", "ropa_tat_000.ytd"));
        var texture = Assert.Single(TextureDictionary.Read(ytd));
        Assert.Equal(("ropa_tat_000", 256, 128, "DXT5"), (texture.Name, texture.Width, texture.Height, texture.Format));

        var item = Assert.Single(XDocument.Load(Path.Combine(Output, "ropa_overlays.xml")).Root!.Element("presetOverlays")!.Elements("Item"));
        Assert.Equal("ropa_tat_000_F", item.Element("nameHash")?.Value);
        Assert.Equal("ZONE_LEFT_ARM", item.Element("zone")?.Value);
        Assert.Equal("GENDER_FEMALE", item.Element("gender")?.Value);
        Assert.Equal("0.250000", item.Element("uvPos")?.Attribute("x")?.Value);
        Assert.Contains("data_file 'PED_OVERLAY_FILE' 'ropa_overlays.xml'", File.ReadAllText(Path.Combine(Output, "fxmanifest.lua")));
        Assert.Contains("ropa_tat_000_F", File.ReadAllText(Path.Combine(Output, "tattoos.json")));
    }

    private string Extra(string name)
    {
        File.WriteAllText(Path.Combine(_dir, name), name);
        return name;
    }

    private static ClothingProject Project(params Drawable[] drawables) =>
        new() { Name = "Ropa 'test'", CollectionName = "ropa", Drawables = [.. drawables] };

    private Drawable Item(string name, PedGender gender, ComponentSlot? component = null, PropSlot? prop = null, int textures = 1)
    {
        File.WriteAllText(Path.Combine(_dir, name + ".ydd"), name + ".ydd");
        var drawable = new Drawable { Name = name, Gender = gender, Component = component, Prop = prop, ModelPath = name + ".ydd" };
        for (var t = 0; t < textures; t++)
        {
            var file = $"{name}_{TextureVariation.LetterFor(t)}.ytd";
            File.WriteAllText(Path.Combine(_dir, file), file);
            drawable.Textures.Add(new TextureVariation { TexturePath = file });
        }
        return drawable;
    }
}
