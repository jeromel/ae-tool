using AeTool.Tests;

// Tests automatisés, sans framework tiers : `dotnet run --project tests/AeTool.Tests`.
// Code de sortie 0 si tout passe, 1 sinon. Options : `--fichier chemin.xml` rejoue les contrôles d'import sur un vrai export Archi ;
// `--v1 chemin.json` migre un vrai référentiel au format 1 et contrôle les effectifs.
// Pour ajouter un domaine : une classe statique avec Run(), puis une ligne ici.

MetamodelTests.Run();
TranscodingTests.Run();
UpgradeTests.Run(args);
ConformityTests.Run();
SpacesTests.Run();
DrawingTests.Run();
ScoringTests.Run();
ImportTests.Run();
ExportTests.Run();
AnalysisTests.Run();
UrbaTests.Run();
ApiTests.Run();
MetamodelApiTests.Run();
RealFileTests.Run(args);
return Harness.Finish();
