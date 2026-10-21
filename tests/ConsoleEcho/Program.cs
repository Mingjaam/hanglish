// Reads typed lines in a real console window and appends them to the file in args[0].
Console.InputEncoding = System.Text.Encoding.UTF8;
while (Console.ReadLine() is { } line) File.AppendAllText(args[0], line + "\n");
