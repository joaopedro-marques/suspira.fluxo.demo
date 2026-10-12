namespace DemoAgencia.Worker.IA.Pipelines;

public static class TomEmocional
{
    public static string Verbal(string fator) => fator switch
    {
        "E" => "Priorize apelo emocional: narrativa aspiracional, conexão humana, storytelling e inspiração.",
        "R" => "Priorize tom racional: clareza, informação prática, objetividade e dados concretos.",
        "ER" => "Busque equilíbrio entre emoção e razão: combine narrativa inspiradora com informação prática e útil.",
        _ => ""
    };

    public static string Visual(string fator) => fator switch
    {
        "E" => "Visual inspirador e acolhedor, atmosfera emotiva, cores quentes e elementos humanos.",
        "R" => "Visual limpo e informativo, direto ao ponto, foco em dados e clareza visual.",
        "ER" => "Busque equilíbrio entre inspiração e informação: visual profissional com toque humano e elementos práticos.",
        _ => ""
    };
}
