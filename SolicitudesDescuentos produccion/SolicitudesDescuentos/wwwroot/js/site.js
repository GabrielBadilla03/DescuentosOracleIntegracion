/*
 * Barra principal de Solicitudes de Descuento.
 * Agrupa Excluir artículos, Desactivar/Reactivar y Agregar descuentos
 * dentro de un popup Bootstrap, como el de Reportes.
 * Se preservan los tres modales originales y sus formularios/scripts.
 */
(() => {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const btnNuevaSolicitud = document.querySelector('button[data-bs-target="#crearModal"]');
        if (!btnNuevaSolicitud) return; // No es la pantalla de solicitudes.

        const barra = btnNuevaSolicitud.closest('.row');
        const bloqueNuevaSolicitud = btnNuevaSolicitud.closest('.col-auto');
        if (!barra || !bloqueNuevaSolicitud) return;
        if (barra.querySelector('#btnConsultarClientes') || barra.querySelector('#btnArticulosDescuentos')) return;

        // Respetar PathBase cuando la aplicación se publica en un directorio virtual de IIS.
        const enlaceInicio = document.querySelector('a.navbar-brand[href]');
        let rutaBase = '/';
        if (enlaceInicio) {
            try {
                let ruta = new URL(enlaceInicio.href, window.location.href).pathname;
                ruta = ruta.replace(/\/+$/, '');
                ruta = ruta.replace(/\/Predescuentos(?:\/Index)?$/i, '');
                rutaBase = ruta + '/';
            } catch (_) {
                // Alternativa: raíz del sitio.
            }
        }

        const bloqueConsulta = document.createElement('div');
        bloqueConsulta.className = 'col-auto';
        const enlaceConsulta = document.createElement('a');
        enlaceConsulta.id = 'btnConsultarClientes';
        enlaceConsulta.className = 'btn btn-danger';
        enlaceConsulta.textContent = 'Consultar clientes';
        enlaceConsulta.href = `${rutaBase.replace(/\/+$/, '')}/ConsultaClientes`;
        bloqueConsulta.appendChild(enlaceConsulta);

        const btnReportes = barra.querySelector('button[data-bs-target="#reportesModal"]');
        const bloqueReportes = btnReportes?.closest('.col-auto');
        barra.insertBefore(bloqueConsulta, bloqueReportes || bloqueNuevaSolicitud);

        // Estos botones se crean en Index.cshtml SOLO si el usuario es PRICE_EDITOR.
        // Nunca crear accesos de artículos para un usuario sin ese rol.
        const acciones = [
            { destino: '#articulosExcluidosModal', texto: 'Excluir artículos' },
            { destino: '#gestionNoPromoModal', texto: 'Desactivar / Reactivar descuentos' },
            { destino: '#copiarDescuentosArticuloModal', texto: 'Agregar descuentos a artículo' }
        ];
        const botonesOriginales = acciones.map(accion =>
            barra.querySelector(`button[data-bs-target="${accion.destino}"]`));

        if (botonesOriginales.some(b => !b)) {
            // Mantener Reportes oculto para usuarios normales.
            bloqueReportes?.remove();
            return;
        }

        // Botón Artículos: abre un modal, igual que el botón Reportes.
        const bloqueArticulos = document.createElement('div');
        bloqueArticulos.className = 'col-auto';
        const btnArticulos = document.createElement('button');
        btnArticulos.type = 'button';
        btnArticulos.id = 'btnArticulosDescuentos';
        btnArticulos.className = 'btn btn-danger';
        btnArticulos.textContent = 'Artículos';
        btnArticulos.setAttribute('data-bs-toggle', 'modal');
        btnArticulos.setAttribute('data-bs-target', '#modalArticulosDescuentos');
        bloqueArticulos.appendChild(btnArticulos);

        // Estructura idéntica al popup de Reportes de la vista.
        const modal = document.createElement('div');
        modal.className = 'modal fade';
        modal.id = 'modalArticulosDescuentos';
        modal.tabIndex = -1;
        modal.setAttribute('aria-labelledby', 'modalArticulosDescuentosTitulo');
        modal.setAttribute('aria-hidden', 'true');

        const dialogo = document.createElement('div');
        dialogo.className = 'modal-dialog';
        const contenido = document.createElement('div');
        contenido.className = 'modal-content shadow';
        const encabezado = document.createElement('div');
        encabezado.className = 'modal-header';
        const titulo = document.createElement('h5');
        titulo.className = 'modal-title';
        titulo.id = 'modalArticulosDescuentosTitulo';
        titulo.textContent = 'Artículos';
        const cerrar = document.createElement('button');
        cerrar.type = 'button';
        cerrar.className = 'btn-close';
        cerrar.setAttribute('data-bs-dismiss', 'modal');
        cerrar.setAttribute('aria-label', 'Cerrar');
        encabezado.append(titulo, cerrar);

        const cuerpo = document.createElement('div');
        cuerpo.className = 'modal-body';
        const lista = document.createElement('div');
        lista.className = 'd-grid gap-2';

        let cambiandoModal = false;
        acciones.forEach(accion => {
            const boton = document.createElement('button');
            boton.type = 'button';
            boton.className = 'btn btn-danger';
            boton.textContent = accion.texto;
            boton.addEventListener('click', () => {
                if (cambiandoModal) return;
                const destino = document.querySelector(accion.destino);
                if (!destino || !window.bootstrap?.Modal) {
                    console.error('No se encontró el modal de artículos:', accion.destino);
                    return;
                }
                cambiandoModal = true;
                // Bootstrap no admite dos modales simultáneos. Esperar que
                // el primero se cierre antes de abrir el formulario solicitado.
                modal.addEventListener('hidden.bs.modal', () => {
                    cambiandoModal = false;
                    window.bootstrap.Modal.getOrCreateInstance(destino).show(btnArticulos);
                }, { once: true });
                window.bootstrap.Modal.getOrCreateInstance(modal).hide();
            });
            lista.appendChild(boton);
        });

        cuerpo.appendChild(lista);
        contenido.append(encabezado, cuerpo);
        dialogo.appendChild(contenido);
        modal.appendChild(dialogo);
        document.body.appendChild(modal);

        barra.insertBefore(bloqueArticulos, bloqueConsulta);
        // Solo retirar botones, NO los modales existentes ni su funcionalidad.
        botonesOriginales.forEach(b => b.closest('.col-auto')?.remove());
    });
})();
