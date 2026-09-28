var workspace=WorkspaceTests.Run();
var operations=DocumentOperationTests.Run();
var passed=workspace.Passed+operations.Passed;var failed=workspace.Failed+operations.Failed;
Directory.CreateDirectory("artifacts/verification");
File.WriteAllText("artifacts/verification/workspace-results.json","{\"passed\":"+passed+",\"failed\":"+failed+"}");
Console.WriteLine($"{passed} workspace tests passed; {failed} failed.");
return failed==0 ? 0 : 1;
