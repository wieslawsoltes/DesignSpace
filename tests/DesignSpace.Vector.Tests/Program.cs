var geometry=VectorPathTests.Run();
var rendering=VectorRenderingTests.Run();
var passed=geometry.Passed+rendering.Passed;
var failed=geometry.Failed+rendering.Failed;
Console.WriteLine($"{passed} vector tests passed; {failed} failed.");
return failed==0 ? 0 : 1;
