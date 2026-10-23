/*
 * Barra principal de SolicitudesDescuentos.
 * La vista original Index.cshtml conserva íntegros sus modales, tablas y scripts.
 * Este archivo solo agrupa sus tres acciones de artículos y agrega acceso
 * de consulta. El HTML original de PRICE_EDITOR determina la visibilidad.
 */
(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        // Solo se ejecuta en la página de solicitudes (no en otras vistas).
        const nuevaSolicitud = document.querySelector('button[data-bs-target="#crearModal"]');
        if (!nuevaSolicitud) return;

        const barra = nuevaSolicitud.closest('.row');
        const bloqueSolicitud = nuevaSolicitud.closest('.col-auto');
        if (!barra || !bloqueSolicitud) return;

        if (barra.querySelector('#menuArticulosDescuentos') || barra.querySelector('#btnConsultarClientes'))
            return;

        // Se respeta automáticamente PathBase si IIS publica la aplicación en
        // un directorio virtual, reutilizando la ruta generada para la marca.
        const enlaceInicio = document.querySelector('a.navbar-brand[href]');
        let rutaBase = '/';
        if (enlaceInicio) {
            try {
                let ruta = new URL(enlaceInicio.href, window.location.href).pathname;
                ruta = ruta.replace(/\/+$/, '');
                ruta = ruta.replace(/\/Predescuentos(?:\/Index)?$/i, '');
                rutaBase = ruta + '/';
            } catch (_) { /* se usa raíz como alternativa */ }
        }
        const consultaUrl = `${rutaBase.replace(/\/+$/, '')}/ConsultaClientes`;

        // Botón para todos los usuarios autenticados (la pantalla está protegida
        // con [Authorize]). Se ubica junto a Nueva Solicitud.
        const bloqueConsulta = document.createElement('div');
        bloqueConsulta.className = 'col-auto';
        const enlaceConsulta = document.createElement('a');
        enlaceConsulta.id = 'btnConsultarClientes';
        enlaceConsulta.className = 'btn btn-outline-danger';
        enlaceConsulta.textContent = 'Consultar clientes';
        enlaceConsulta.href = consultaUrl;
        bloqueConsulta.appendChild(enlaceConsulta);
        const botonReportesOriginal = barra.querySelector('button[data-bs-target="#reportesModal"]');
        const posicionConsulta = botonReportesOriginal?.closest('.col-auto') || bloqueSolicitud;
        barra.insertBefore(bloqueConsulta, posicionConsulta);

        // Si los tres botones originales no existen, el usuario NO tiene el
        // rol PRICE_EDITOR. No se construye el menú Artículos.
        const acciones = [
            ['#articulosExcluidosModal', 'Excluir artículos'],
            ['#gestionNoPromoModal', 'Desactivar / Reactivar descuentos'],
            ['#copiarDescuentosArticuloModal', 'Agregar descuentos a artículo']
        ];
        const botones = acciones.map(([selector]) =>
            barra.querySelector(`button[data-bs-target="${selector}"]`));

        if (botones.some(b => !b)) {
            // Un usuario sin PRICE_EDITOR solo debe ver Consultar clientes
            // y Nueva solicitud. La autorización real se exige en el servidor.
            botonReportesOriginal?.closest('.col-auto')?.remove();
            return;
        }

        const bloqueArticulos = document.createElement('div');
        bloqueArticulos.className = 'col-auto dropdown';
        bloqueArticulos.id = 'menuArticulosDescuentos';

        const disparador = document.createElement('button');
        disparador.type = 'button';
        disparador.className = 'btn btn-danger dropdown-toggle';
        disparador.id = 'descuentosArticulosDropdown';
        disparador.textContent = 'Artículos';
        disparador.setAttribute('data-bs-toggle', 'dropdown');
        disparador.setAttribute('aria-expanded', 'false');
        bloqueArticulos.appendChild(disparador);

        const lista = document.createElement('ul');
        lista.className = 'dropdown-menu shadow';
        lista.setAttribute('aria-labelledby', disparador.id);

        for (const [selector, texto] of acciones) {
            const li = document.createElement('li');
            const boton = document.createElement('button');
            boton.type = 'button';
            boton.className = 'dropdown-item';
            boton.textContent = texto;
            boton.setAttribute('data-bs-toggle', 'modal');
            boton.setAttribute('data-bs-target', selector);
            li.appendChild(boton);
            lista.appendChild(li);
        }

        bloqueArticulos.appendChild(lista);
        barra.insertBefore(bloqueArticulos, bloqueConsulta);

        // Eliminar solo los botones anteriores, no sus modales ni sus scripts.
        for (const boton of botones) boton.closest('.col-auto')?.remove();
    });
})();
