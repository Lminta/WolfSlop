var assertions = EngineChecks.Run();
assertions += MapChecks.Run();
assertions += CarPhysicsChecks.Run();
assertions += CarContactChecks.Run();
assertions += CarCameraChecks.Run();
assertions += SpriteAnimationChecks.Run();
assertions += AudioChecks.Run();

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "WolfSlop.slnx"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Could not find the engine repository root.");

Console.WriteLine($"PASS: {assertions} engine assertions (maps, walking, input, ownership, driving, cameras, animation, and audio).");
