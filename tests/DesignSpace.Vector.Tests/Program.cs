var geometry=VectorPathTests.Run();
var rendering=VectorRenderingTests.Run();
var safety=VectorSafetyTests.Run();
var passed=geometry.Passed+rendering.Passed+safety.Passed;
var failed=geometry.Failed+rendering.Failed+safety.Failed;
Directory.CreateDirectory("artifacts/verification");
File.WriteAllText("artifacts/verification/vector-results.json",System.Text.Json.JsonSerializer.Serialize(new { passed,failed,geometryPassed=geometry.Passed,renderingPassed=rendering.Passed,safetyAndCachePassed=safety.Passed }));
Console.WriteLine($"{passed} vector tests passed; {failed} failed.");
return failed==0 ? 0 : 1;
