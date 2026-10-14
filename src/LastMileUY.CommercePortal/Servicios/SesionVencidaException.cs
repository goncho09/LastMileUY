namespace LastMileUY.CommercePortal.Servicios;

// La API respondió 401: la sesión venció o no es válida. Hay que volver a iniciar sesión.
public class SesionVencidaException() : Exception("La sesión con la API venció o no es válida.");
