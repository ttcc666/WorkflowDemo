if (args.Length != 1 || args[0] is not ("seed" or "auth" or "restore" or "network")) throw new ArgumentException("Use one explicit stage."); await Approval.RehearsalTool.Rehearsal.RunAsync(args[0]);
