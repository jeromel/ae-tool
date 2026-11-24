using AeTool.Tests;

// Tests automatisés, sans framework tiers : `dotnet run --project tests/AeTool.Tests`.
// Code de sortie 0 si tout passe, 1 sinon. Options : `--fichier chemin.xml` rejoue les contrôles d'import sur un vrai export Archi ;
// `--v1 chemin.json` migre un vrai référentiel au format 1 et contrôle les effectifs.
// Pour ajouter un domaine : une classe statique avec Run(), puis une ligne ici.

MetamodelTests.Run();
TranscodingTests.Run();
UpgradeTests.Run(args);
HistoryTests.Run();
ValidationTests.Run(args);
StorageTests.Run();
ConformityTests.Run();
MasteryTests.Run();
SpacesTests.Run();
DrawingTests.Run();
ScoringTests.Run();
ImportTests.Run();
ExportTests.Run();
AnalysisTests.Run();
UrbaTests.Run();
ReconciliationTests.Run();
TabularTests.Run();
RestSourceClientTests.Run();
SourcesApiTests.Run();
HostingTests.Run();
AccessRulesTests.Run();
AuthTests.Run();
ApiTests.Run();
MetamodelApiTests.Run();
EditingTests.Run();
WorkflowApiTests.Run();
MetamodelInsightTests.Run();
DrawioTests.Run();
LayoutTests.Run();
TrajectoryTests.Run();
ExampleDatasetTests.Run();
RealFileTests.Run(args);
return Harness.Finish();
