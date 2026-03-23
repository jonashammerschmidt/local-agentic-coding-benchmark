using LocalAgenticCodingBenchmark.Cli;

var exitCode = await Cli.RunAsync(args);
Environment.ExitCode = exitCode;
