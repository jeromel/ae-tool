using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AeTool.Api;
using AeTool.Api.Storage;
using AeTool.Core;
using static AeTool.Tests.Harness;

namespace AeTool.Tests;

/// <summary>Stockage durci : journal séparé en ajout seul, écriture sûre, sauvegardes, restauration. Sans HTTP, dans des dossiers temporaires.</summary>
public static class StorageTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2025, 5, 2, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Dir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ae-storage-" + Guid.NewGuid().ToString("N"));
        public string Repository => System.IO.Path.Combine(Path, "referentiel.json");
        public string Journal => System.IO.Path.Combine(Path, "journal.jsonl");
        public string Backups => System.IO.Path.Combine(Path, "sauvegardes");
        public Dir() => Directory.CreateDirectory(Path);
        public JsonFileStore Store(int keep = 40, int daily = 30) => new(Repository, keep, daily);
        public Workspace Workspace(Clock clock, int keep = 40, int daily = 30) => new(Store(keep, daily), clock);
        public string[] JournalLines() => File.Exists(Journal) ? File.ReadAllLines(Journal).Where(l => l.Length > 0).ToArray() : Array.Empty<string>();
        public string[] BackupNames() => Directory.Exists(Backups) ? Directory.GetFiles(Backups).Select(f => System.IO.Path.GetFileName(f)).Order().ToArray() : Array.Empty<string>();
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { /* sans conséquence */ } }
    }

    private static void AddObject(Workspace ws, string id, string name) =>
        ws.Write(repo =>
        {
            repo.Objects[id] = new ArchObject { Id = id, TypeKey = "application", Name = name };
            repo.Log("ann", "Objet créé", JournalKinds.Object, id, null, name);
            return 0;
        });

    public static void Run()
    {
        Console.WriteLine("Stockage : journal séparé, écriture sûre, sauvegardes");

        Test("le journal n'est plus dans le fichier du référentiel : il est dans journal.jsonl, relu au démarrage", () =>
        {
            using var d = new Dir();
            var store = d.Store();
            var repo = store.Load();
            repo.Log("ann", "Objet créé", JournalKinds.Object, "o1", new[] { new FieldChange("Nom", null, "A") }, "A");
            repo.Log("bob", "Import Archi");
            store.Save(repo);
            True(!JsonNode.Parse(File.ReadAllText(d.Repository))!.AsObject().ContainsKey("Journal"), "pas de Journal dans referentiel.json");
            Equal(2, d.JournalLines().Length, "une ligne par entrée");
            var again = d.Store().Load();
            Equal(2, again.Journal.Count, "entrées relues");
            Equal(new FieldChange("Nom", null, "A"), again.Journal[0].Changes![0], "avant/après relus");
        });

        Test("ajout seul : une écriture n'ajoute que les nouvelles lignes, les anciennes restent à l'octet près", () =>
        {
            using var d = new Dir();
            var store = d.Store();
            var repo = store.Load();
            repo.Log("ann", "Premier");
            store.Save(repo);
            var first = File.ReadAllBytes(d.Journal);
            repo.Log("ann", "Second");
            store.Save(repo);
            var second = File.ReadAllBytes(d.Journal);
            True(second.Length > first.Length && second.Take(first.Length).SequenceEqual(first), "préfixe conservé");
            store.Save(repo);
            Equal(second.Length, File.ReadAllBytes(d.Journal).Length, "une sauvegarde sans nouvelle entrée n'ajoute rien");
        });

        Test("aucun plafond : 5 200 entrées sont conservées et relues", () =>
        {
            using var d = new Dir();
            var store = d.Store();
            var repo = store.Load();
            for (var i = 0; i < 5200; i++) repo.Log("ann", "Objet modifié", $"o{i}");
            store.Save(repo);
            Equal(5200, d.Store().Load().Journal.Count, "entrées relues");
        });

        Test("aucune perte si l'application s'arrête entre deux écritures : l'entrée ajoutée seule est là au redémarrage", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock);
            AddObject(ws, "o1", "A");
            ws.Append(new JournalEntry { At = DateTime.UtcNow, User = "ann", Action = "Connexion", TargetKind = JournalKinds.Account });
            // Arrêt brutal simulé : aucune autre écriture, on repart du dossier tel quel.
            var restarted = d.Workspace(clock);
            True(restarted.Read((repo, _) => repo.Objects.ContainsKey("o1")), "objet écrit");
            Equal(2, restarted.Read((repo, _) => repo.Journal.Count), "journal : création et connexion");
        });

        Test("écriture légère : Append n'écrit qu'une ligne et ne touche pas au fichier du référentiel", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            AddObject(ws, "o1", "A");
            var referentiel = File.ReadAllBytes(d.Repository);
            var lines = d.JournalLines().Length;
            ws.Append(new JournalEntry { At = DateTime.UtcNow, User = "ann", Action = "Connexion" });
            Equal(lines + 1, d.JournalLines().Length, "une ligne de plus");
            True(File.ReadAllBytes(d.Repository).SequenceEqual(referentiel), "référentiel intact");
        });

        Test("dernière ligne tronquée par un arrêt brutal : ignorée, et l'ajout suivant ne s'y colle pas", () =>
        {
            using var d = new Dir();
            var store = d.Store();
            var repo = store.Load();
            repo.Log("ann", "Premier");
            store.Save(repo);
            File.AppendAllText(d.Journal, "{\"At\":\"2025-01-01T00:00:00Z\",\"User\":\"an");
            var reloaded = d.Store();
            var repo2 = reloaded.Load();
            Equal(1, repo2.Journal.Count, "ligne tronquée ignorée");
            repo2.Log("ann", "Suivant");
            reloaded.Save(repo2);
            var final = d.Store().Load();
            Equal("Premier,Suivant", string.Join(",", final.Journal.Select(e => e.Action)), "entrées saines relues");
        });

        Test("transfert : un référentiel qui portait son journal le passe à journal.jsonl à la première sauvegarde, sans doublon", () =>
        {
            using var d = new Dir();
            var json = JsonSerializer.Serialize(new Repository { Journal = { new JournalEntry { At = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), User = "ancien", Action = "Objet créé" } } });
            File.WriteAllText(d.Repository, json);
            var store = d.Store();
            var repo = store.Load();
            Equal(1, repo.Journal.Count, "journal d'origine lu");
            repo.Log("ann", "Nouveau");
            store.Save(repo);
            Equal(2, d.JournalLines().Length, "journal transféré puis complété");
            True(!JsonNode.Parse(File.ReadAllText(d.Repository))!.AsObject().ContainsKey("Journal"), "plus de journal dans le référentiel");
            Equal(2, d.Store().Load().Journal.Count, "relu sans doublon");
        });

        Test("transfert interrompu : journal.jsonl déjà écrit mais le référentiel porte encore le sien, aucun doublon ni perte", () =>
        {
            using var d = new Dir();
            var old = new JournalEntry { At = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), User = "ancien", Action = "Objet créé" };
            File.WriteAllText(d.Repository, JsonSerializer.Serialize(new Repository { Journal = { old } }));
            File.WriteAllText(d.Journal, JsonSerializer.Serialize(old) + "\n" + JsonSerializer.Serialize(new JournalEntry { At = DateTime.UtcNow, User = "ann", Action = "Après" }) + "\n");
            var repo = d.Store().Load();
            Equal("Objet créé,Après", string.Join(",", repo.Journal.Select(e => e.Action)), "réunion sans doublon");
        });

        Test("migration de schéma : le journal d'un fichier v1 est transféré, l'original est copié sous le numéro de sa version", () =>
        {
            using var d = new Dir();
            var v2 = JsonNode.Parse(JsonSerializer.Serialize(new Repository { Journal = { new JournalEntry { At = DateTime.UtcNow, User = "ancien", Action = "Objet créé" } } }))!.AsObject();
            v2["SchemaVersion"] = 2;
            File.WriteAllText(d.Repository, v2.ToJsonString());
            var repo = d.Store().Load();
            True(File.Exists(Path.Combine(d.Path, "referentiel.avant-migration-v2.json")), "copie de l'original d'une version 2");
            Equal(1, d.JournalLines().Length, "journal transféré dès la migration");
            Equal(Repository.CurrentSchemaVersion, JsonNode.Parse(File.ReadAllText(d.Repository))!["SchemaVersion"]!.GetValue<int>(), "version écrite");
            Equal(1, repo.Journal.Count, "journal en mémoire");
        });

        Test("écriture sûre : pas de fichier temporaire après l'écriture, un reste d'une coupure est écrasé", () =>
        {
            using var d = new Dir();
            File.WriteAllText(d.Repository + ".tmp", "reste tronqué d'une coupure");
            var store = d.Store();
            var repo = store.Load();
            repo.Objects["o1"] = new ArchObject { Id = "o1", TypeKey = "application", Name = "A" };
            store.Save(repo);
            True(!File.Exists(d.Repository + ".tmp"), "fichier temporaire remplacé");
            Equal("A", d.Store().Load().Objects["o1"].Name, "contenu relu");
        });

        Test("sauvegarde quotidienne : une seule par jour, à la première écriture, de l'état d'avant", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock);
            AddObject(ws, "o1", "A");
            Equal(1, d.BackupNames().Length, "première écriture du jour");
            True(d.BackupNames()[0].StartsWith("referentiel-20250502-090000-auto"), "nom : date, heure UTC, motif : " + d.BackupNames()[0]);
            AddObject(ws, "o2", "B");
            Equal(1, d.BackupNames().Length, "pas de seconde le même jour");
            clock.Now = clock.Now.AddDays(1);
            AddObject(ws, "o3", "C");
            Equal(2, d.BackupNames().Length, "le lendemain");
            var restarted = d.Workspace(clock);
            AddObject(restarted, "o4", "D");
            Equal(2, d.BackupNames().Length, "un redémarrage le même jour n'en ajoute pas");
            var content = new JsonFileStore(d.Repository).ReadBackup(Path.Combine(d.Backups, d.BackupNames()[1]));
            Equal(2, content.Objects.Count, "la sauvegarde du lendemain est celle de l'état d'avant l'écriture (o1, o2)");
        });

        Test("avant une opération risquée : sauvegarde au motif de l'opération, même si la quotidienne existe", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock);
            AddObject(ws, "o1", "A");
            ws.Write(repo => 0, BackupReasons.ArchiImport);
            clock.Now = clock.Now.AddSeconds(5);
            ws.Write(repo => 0, BackupReasons.MetamodelImport);
            var names = d.BackupNames();
            Equal(3, names.Length, "auto + deux opérations");
            True(names.Any(n => n.EndsWith("-import-archi.json.gz")) && names.Any(n => n.EndsWith("-import-metamodele.json.gz")), "motifs : " + string.Join(", ", names));
        });

        Test("deux sauvegardes dans la même seconde ne s'écrasent pas", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            ws.Backup("manuelle", "ann");
            ws.Backup("manuelle", "ann");
            Equal(2, d.BackupNames().Length, "fichiers");
            Equal(2, ws.Backups().Count, "liste");
        });

        Test("rotation : seules les plus récentes sont gardées (réglage Storage:BackupsToKeep)", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock, keep: 3);
            for (var i = 0; i < 6; i++)
            {
                clock.Now = clock.Now.AddMinutes(1);
                ws.Backup("manuelle", "ann");
            }
            var kept = ws.Backups();
            Equal(3, kept.Count, "conservées");
            Equal("090600,090500,090400", string.Join(",", kept.Select(b => b.At.ToString("HHmmss"))), "les trois plus récentes, la plus récente d'abord");
        });

        Test("rotation : les sauvegardes quotidiennes ont leur propre quota, une série d'opérations ne les évince pas", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock, keep: 2, daily: 2);
            for (var day = 0; day < 3; day++)
            {
                AddObject(ws, "o" + day, "Objet " + day);          // première écriture du jour : sauvegarde quotidienne
                for (var i = 0; i < 3; i++)
                {
                    clock.Now = clock.Now.AddMinutes(1);
                    ws.Backup("manuelle", "ann");
                }
                clock.Now = clock.Now.AddDays(1);
            }
            var kept = ws.Backups();
            Equal(2, kept.Count(b => b.Reason == "auto"), "deux quotidiennes gardées malgré neuf sauvegardes manuelles");
            Equal(2, kept.Count(b => b.Reason != "auto"), "deux manuelles gardées");
            Equal(2, kept.Where(b => b.Reason == "auto").Select(b => b.At.Date).Distinct().Count(), "les deux derniers jours");
        });

        Test("restauration : l'état d'origine revient, une sauvegarde de précaution est prise, le journal n'est pas rembobiné", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock);
            AddObject(ws, "o1", "A");
            var snapshot = ws.Backup("manuelle", "ann");
            AddObject(ws, "o2", "B");
            ws.Write(repo => { repo.Objects["o1"].Name = "A modifié"; return 0; });
            clock.Now = clock.Now.AddMinutes(1);
            var before = ws.Read((repo, _) => repo.Journal.Count);
            Equal(snapshot.Name, ws.Restore(snapshot.Name, "adm")!.Name, "restauration");
            Equal("A", ws.Read((repo, _) => repo.Objects["o1"].Name), "nom d'origine");
            True(ws.Read((repo, _) => !repo.Objects.ContainsKey("o2")), "objet créé après la sauvegarde disparu");
            True(d.BackupNames().Any(n => n.Contains("-avant-restauration")), "sauvegarde de précaution");
            True(ws.Read((repo, _) => repo.Journal.Count > before && repo.Journal[^1].Action == "Sauvegarde restaurée"), "journal complété, restauration tracée");
            var restarted = d.Workspace(clock);
            Equal("A", restarted.Read((repo, _) => repo.Objects["o1"].Name), "état restauré sur le disque");
            Equal(ws.Read((repo, _) => repo.Journal.Count), restarted.Read((repo, _) => repo.Journal.Count), "journal identique après redémarrage");
        });

        Test("restauration d'une sauvegarde de schéma plus ancien : lue par le migrateur", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            AddObject(ws, "o1", "A");
            var info = ws.Backup("manuelle", "ann");
            var path = Path.Combine(d.Backups, info.Name);
            JsonNode root;
            using (var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress)) root = JsonNode.Parse(gz)!;
            root["SchemaVersion"] = 2;
            root["Objects"]!["o1"]!["Source"] = "archi";
            using (var gz = new GZipStream(File.Create(path), CompressionLevel.Optimal)) gz.Write(Encoding.UTF8.GetBytes(root.ToJsonString()));
            ws.Restore(info.Name, "adm");
            Equal("Validé", ws.Read((repo, _) => repo.Objects["o1"].ValidationStatus), "migration des statuts appliquée à la restauration");
        });

        Test("sauvegarde illisible : la restauration échoue et rien n'est modifié", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            AddObject(ws, "o1", "A");
            var info = ws.Backup("manuelle", "ann");
            File.WriteAllText(Path.Combine(d.Backups, info.Name), "pas du gzip");
            var failed = false;
            try { ws.Restore(info.Name, "adm"); } catch (Exception e) when (e is InvalidDataException or JsonException) { failed = true; }
            True(failed, "erreur de lecture signalée");
            Equal("A", ws.Read((repo, _) => repo.Objects["o1"].Name), "état intact");
        });

        Test("nom de sauvegarde malveillant : refusé, jamais ouvert", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            AddObject(ws, "o1", "A");
            File.WriteAllText(Path.Combine(d.Path, "secret.json.gz"), "secret");
            var valid = ws.Backups()[0].Name;
            foreach (var name in new[] { "../secret.json.gz", "..\\secret.json.gz", "/etc/passwd", Path.Combine(d.Path, "secret.json.gz"), "", "referentiel.json",
                         "sauvegardes/" + valid, "./" + valid, valid + "/../../secret.json.gz", "referentiel-20250101-000000-auto.json.gz", "referentiel-20250502-090000-../x.json.gz", valid + "\0.txt" })
            {
                Equal(null, ws.BackupPath(name), $"chemin « {name.Replace("\0", "\\0")} »");
                Equal(null, ws.Restore(name, "adm"), $"restauration « {name.Replace("\0", "\\0")} »");
            }
            True(ws.BackupPath(valid) is not null, "un vrai nom est accepté");
        });

        Test("la sauvegarde contient le référentiel et son journal, en gzip lisible", () =>
        {
            using var d = new Dir();
            var ws = d.Workspace(new Clock());
            AddObject(ws, "o1", "A");
            var info = ws.Backup("manuelle", "ann");
            using var gz = new GZipStream(File.OpenRead(Path.Combine(d.Backups, info.Name)), CompressionMode.Decompress);
            var root = JsonNode.Parse(gz)!.AsObject();
            True(root.ContainsKey("Objects") && root["Journal"]!.AsArray().Count >= 1, "objets et journal présents");
        });

        Test("remise à zéro : sauvegarde d'abord, le journal est conservé et la trace ajoutée", () =>
        {
            using var d = new Dir();
            var clock = new Clock();
            var ws = d.Workspace(clock);
            AddObject(ws, "o1", "A");
            ws.Reset("adm");
            True(d.BackupNames().Any(n => n.Contains("-remise-a-zero")), "sauvegarde avant remise à zéro");
            Equal(0, ws.Read((repo, _) => repo.Objects.Count), "référentiel vide");
            var actions = ws.Read((repo, _) => repo.Journal.Select(e => e.Action).ToList());
            True(actions.Contains("Objet créé") && actions.Contains("Remise à zéro du référentiel"), "journal conservé : " + string.Join(", ", actions));
            Equal(actions.Count, d.Workspace(clock).Read((repo, _) => repo.Journal.Count), "relu au redémarrage");
        });
    }
}
