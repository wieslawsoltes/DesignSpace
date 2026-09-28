var geometry=VectorPathTests.Run();
var rendering=VectorRenderingTests.Run();
var safety=VectorSafetyTests.Run();
var passed=geometry.Passed+rendering.Passed+safety.Passed;
var failed=geometry.Failed+rendering.Failed+safety.Failed;
Console.WriteLine($"{passed} vector tests passed; {failed} failed.");
return failed==0 ? 0 : 1;
