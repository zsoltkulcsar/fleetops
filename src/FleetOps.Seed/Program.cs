using FleetOps.Seed;

public static class SeedProgram
{
    public static async Task Main()
    {
        try
        {
            await new DemoSeeder().RunAsync();
            Console.WriteLine("Demo seed completed.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Environment.ExitCode = 1;
        }
    }
}
