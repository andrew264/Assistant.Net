namespace Assistant.Net.Services.Games.Models;

public enum EvenOddChoice
{
    Even = 0,
    Odd = 1
}

public class TossNumberChoices
{
    public int? Player1Number { get; set; }
    public int? Player2Number { get; set; }
    public EvenOddChoice Player1ChoicePreference { get; set; }
}

public class GameNumberChoices
{
    public int? Player1Number { get; set; }
    public int? Player2Number { get; set; }
}