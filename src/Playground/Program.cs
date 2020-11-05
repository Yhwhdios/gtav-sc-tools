﻿namespace ScTools.Playground
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using CodeWalker.GameFiles;

    using ScTools;
    using ScTools.GameFiles;
    using ScTools.ScriptLang;
    using ScTools.ScriptLang.Semantics;
    using ScTools.ScriptLang.Semantics.Symbols;

    internal static class Program
    {
        private static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            LoadGTA5Keys();
            DoTest();
        }

        private static void LoadGTA5Keys()
        {
            string path = ".\\Keys";
            GTA5Keys.PC_AES_KEY = File.ReadAllBytes(path + "\\gtav_aes_key.dat");
            GTA5Keys.PC_NG_KEYS = CryptoIO.ReadNgKeys(path + "\\gtav_ng_key.dat");
            GTA5Keys.PC_NG_DECRYPT_TABLES = CryptoIO.ReadNgTables(path + "\\gtav_ng_decrypt_tables.dat");
            GTA5Keys.PC_NG_ENCRYPT_TABLES = CryptoIO.ReadNgTables(path + "\\gtav_ng_encrypt_tables.dat");
            GTA5Keys.PC_NG_ENCRYPT_LUTs = CryptoIO.ReadNgLuts(path + "\\gtav_ng_encrypt_luts.dat");
            GTA5Keys.PC_LUT = File.ReadAllBytes(path + "\\gtav_hash_lut.dat");
        }

        const string Code = @"
SCRIPT_NAME test_script

NATIVE PROC WAIT(INT ms)
NATIVE FUNC INT GET_GAME_TIMER()
NATIVE PROC BEGIN_TEXT_COMMAND_DISPLAY_TEXT(STRING text)
NATIVE PROC END_TEXT_COMMAND_DISPLAY_TEXT(FLOAT x, FLOAT y, INT p2)
NATIVE PROC ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME(STRING text)
NATIVE PROC ADD_TEXT_COMPONENT_INTEGER(INT value)
NATIVE PROC ADD_TEXT_COMPONENT_FLOAT(FLOAT value, INT decimalPlaces)
NATIVE FUNC FLOAT TIMESTEP()
NATIVE FUNC BOOL IS_CONTROL_PRESSED(INT padIndex, INT control)
NATIVE FUNC FLOAT VMAG(VEC3 v)
NATIVE FUNC FLOAT VMAG2(VEC3 v)
NATIVE FUNC FLOAT VDIST(VEC3 v1, VEC3 v2)
NATIVE FUNC FLOAT VDIST2(VEC3 v1, VEC3 v2)
NATIVE FUNC VEC3 GET_GAMEPLAY_CAM_COORD()

STRUCT SPAWNPOINT
    FLOAT heading
    VEC3 position
ENDSTRUCT

SPAWNPOINT sp = <<45.0, <<100.0, 200.0, 50.0>>>>
FLOAT v = 123.456 + 789.0

PROC MAIN()
    SPAWNPOINT sp2 = <<sp.heading, GET_POS()>>

    WHILE TRUE
        WAIT(0)

        VEC3 pos = GET_GAMEPLAY_CAM_COORD()
        FLOAT dist = VDIST(pos, sp.position)

        DRAW_STRING(0.5, 0.05, ""Gameplay Cam Position"")
        DRAW_FLOAT(0.25, 0.15, pos.x)
        DRAW_FLOAT(0.5, 0.15, pos.y)
        DRAW_FLOAT(0.75, 0.15, pos.z)

        DRAW_STRING(0.5, 0.35, ""Distance"")
        DRAW_FLOAT(0.5, 0.45, dist)
    ENDWHILE
ENDPROC

FUNC VEC3 GET_POS()
    RETURN <<100.0, 100.0, 60.0>>
ENDFUNC

PROC DRAW_STRING(FLOAT x, FLOAT y, STRING v)
    BEGIN_TEXT_COMMAND_DISPLAY_TEXT(""STRING"")
    ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME(v)
    END_TEXT_COMMAND_DISPLAY_TEXT(x, y, 0)
ENDPROC

PROC DRAW_FLOAT(FLOAT x, FLOAT y, FLOAT v)
    BEGIN_TEXT_COMMAND_DISPLAY_TEXT(""NUMBER"")
    ADD_TEXT_COMPONENT_FLOAT(v, 2)
    END_TEXT_COMMAND_DISPLAY_TEXT(x, y, 0)
ENDPROC
";

        public static void DoTest()
        {
            //NativeDB.Fetch(new Uri("https://raw.githubusercontent.com/alloc8or/gta5-nativedb-data/master/natives.json"), "ScriptHookV_1.0.2060.1.zip")
            //    .ContinueWith(t => File.WriteAllText("nativedb.json", t.Result.ToJson()))
            //    .Wait();

            var nativeDB = NativeDB.FromJson(File.ReadAllText("nativedb.json"));

            using var reader = new StringReader(Code);
            var module = Module.Compile(reader, nativeDB: nativeDB);
            File.WriteAllText("test_script.ast.txt", module.GetAstDotGraph());

            var d = module.Diagnostics;
            var symbols = module.SymbolTable;
            Console.WriteLine($"Errors:   {d.HasErrors} ({d.Errors.Count()})");
            Console.WriteLine($"Warnings: {d.HasWarnings} ({d.Warnings.Count()})");
            foreach (var diagnostic in d.AllDiagnostics)
            {
                diagnostic.Print(Console.Out);
            }

            foreach (var s in symbols.Symbols)
            {
                if (s is TypeSymbol t && t.Type is StructType struc)
                {
                    Console.WriteLine($"  > '{t.Name}' Size = {struc.SizeOf}");
                }
            }

            Console.WriteLine();
            new Dumper(module.CompiledScript).Dump(Console.Out, true, true, true, true, true);

            YscFile ysc = new YscFile
            {
                Script = module.CompiledScript
            };

            string outputPath = "test_script.ysc";
            byte[] data = ysc.Save(Path.GetFileName(outputPath));
            File.WriteAllBytes(outputPath, data);

            outputPath = Path.ChangeExtension(outputPath, "unencrypted.ysc");
            data = ysc.Save();
            File.WriteAllBytes(outputPath, data);
            ;
        }
    }
}
