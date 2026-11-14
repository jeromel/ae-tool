using AeTool.Tests;

// Tests automatisés, sans framework tiers : `dotnet run --project tests/AeTool.Tests`.
// Code de sortie 0 si tout passe, 1 sinon. Option : `--fichier chemin.xml` rejoue les contrôles d'import sur un vrai export Archi.
// Pour ajouter un domaine : une classe statique avec Run(), puis une ligne ici.

SpacesTests.Run();
DrawingTests.Run();
ScoringTests.Run();
ImportTests.Run();
ExportTests.Run();
AnalysisTests.Run();
UrbaTests.Run();
ReconciliationTests.Run();
TabularTests.Run();
EasyVistaClientTests.Run();
SourcesApiTests.Run();
ApiTests.Run();
RealFileTests.Run(args);
return Harness.Finish();
