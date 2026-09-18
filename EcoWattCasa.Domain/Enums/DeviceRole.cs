namespace EcoWattCasa.Domain.Enums;

/// <summary>
/// Que representa lo que mide el dispositivo. Separado del modelo de hardware porque de esto
/// depende como entra al calculo: el total de la casa sale del medidor de tablero, y los
/// enchufes son el desglose de ese total.
/// </summary>
public enum DeviceRole
{
    /// <summary>
    /// Medidor en el tablero principal. Su lectura es el consumo TOTAL de la casa e incluye
    /// lo que consumen los enchufes, porque esta aguas arriba de todos ellos.
    /// Solo puede haber uno activo.
    /// </summary>
    HouseMeter = 0,

    /// <summary>
    /// Equipo puntual medido por su propio enchufe. Es desglose del total, no se suma a el.
    /// </summary>
    Appliance = 1
}
