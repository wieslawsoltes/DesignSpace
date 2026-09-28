var result=WorkspaceTests.Run();
Console.WriteLine($"{result.Passed} workspace tests passed; {result.Failed} failed.");
return result.Failed==0 ? 0 : 1;
