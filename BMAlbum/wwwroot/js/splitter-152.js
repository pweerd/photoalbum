/*
* jQuery.splitter.js - two-pane splitter window plugin
*
* version 1.6 (2010/01/03) 
* 
* Dual licensed under the MIT and GPL licenses: 
*   http://www.opensource.org/licenses/mit-license.php 
*   http://www.gnu.org/licenses/gpl.html 
*/

/**
* The splitter() plugin implements a two-pane resizable splitter window.
* The selected elements in the jQuery object are converted to a splitter;
* each selected element should have two child elements, used for the panes
* of the splitter. The plugin adds a third child element for the splitbar.
* 
* For more details see: http://methvin.com/jquery/splitter/
*
*
* @example $('#MySplitter').splitter();
* @desc Create a vertical splitter with default settings 
*
* @example $('#MySplitter').splitter({type: 'h', accessKey: 'M'});
* @desc Create a horizontal splitter resizable via Alt+Shift+M
*
* @name splitter
* @type jQuery
* @param Object options Options for the splitter (not required)
* @cat Plugins/Splitter
* @return jQuery
* @author Dave Methvin (dave.methvin@gmail.com)
*/
; (function ($) {

   var splitterCounter = 0;

   $.fn.splitter = function (args) {
      args = args || {};
      return this.each(function () {
         if ($(this).is(".splitter"))	// already a splitter
            return;
         var zombie; 	// left-behind splitbar for outline resizes
         var updateTimer;
         function triggerUpdated() {
            if (updateTimer) clearTimeout(updateTimer);
            updateTimer = setTimeout(function () {
               splitter.trigger("splitter_updated");
            }, 200);
         }
         function setBarState(state) {
            bar.removeClass(opts.barStateClasses).addClass(state);
         }
         function startSplitMouse(evt) {
            if (evt.which !== 1)
               return; 	// left button only
            bar.removeClass(opts.barHoverClass);
            if (opts.outline) {
               zombie = zombie || bar.clone(false).insertAfter(A);
               bar.removeClass(opts.barDockedClass);
            }
            setBarState(opts.barActiveClass)
            // Safari selects A/B text on a move; iframes capture mouse events so hide them
            panes.css("-webkit-user-select", "none").find("iframe").addClass(opts.iframeClass);
            A._posSplit = A[0][opts.pxSplit] - evt[opts.eventPos];
            $(document)
               .bind("mousemove" + opts.eventNamespace, doSplitMouse)
               .bind("mouseup" + opts.eventNamespace, endSplitMouse);
         }
         function doSplitMouse(evt) {
            var pos = A._posSplit + evt[opts.eventPos],
               range = Math.max(0, Math.min(pos, splitter._DA - bar._DA)),
               limit = Math.max(A._min, splitter._DA - B._max,
                  Math.min(pos, A._max, splitter._DA - bar._DA - B._min));
            if (opts.outline) {
               // Let docking splitbar be dragged to the dock position, even if min width applies
               if ((opts.dockPane === A && pos < Math.max(A._min, bar._DA)) ||
                  (opts.dockPane === B && pos > Math.min(pos, A._max, splitter._DA - bar._DA - B._min))) {
                  bar.addClass(opts.barDockedClass).css(opts.origin, range);
               }
               else {
                  bar.removeClass(opts.barDockedClass).css(opts.origin, limit);
               }
               bar._DA = bar[0][opts.pxSplit];
            } else
               resplit(pos);
            setBarState(pos === limit ? opts.barActiveClass : opts.barLimitClass);
         }
         function endSplitMouse(evt) {
            setBarState(opts.barNormalClass);
            bar.addClass(opts.barHoverClass);
            var pos = A._posSplit + evt[opts.eventPos];
            if (opts.outline) {
               zombie.remove(); zombie = null;
               resplit(pos);
            }
            var cs = splitter._clientSize();
            var docked = bar._docked ? true : false;
            if (pos > 5 && cs - pos > 5) {
               bar._docked = false;
               adjustPaneAdmin(pos);
            } else {
               bar._docked = true;
               pos = (pos <= 5) ? 0 : cs; 
               resplit(pos);
               triggerUpdated();
            }
            if (docked !== bar._docked) bar._lastDockWasManual = true;

            panes.css("-webkit-user-select", "text").find("iframe").removeClass(opts.iframeClass);
            $(document)
               .unbind("mousemove" + opts.eventNamespace + " mouseup" + opts.eventNamespace);
         }
         function adjustPaneAdmin(pos) {
            bar._DA = bar[0][opts.pxSplit]; 	// bar size may change during dock
            // Constrain new splitbar position to fit pane size and docking limits
            if ((opts.dockPane === A && pos < Math.max(A._min, bar._DA)) ||
               (opts.dockPane === B && pos > Math.min(pos, A._max, splitter._DA - bar._DA - B._min))) {
               pos = opts.dockPane === A ? 0 : splitter._DA - bar._DA;
            } else {
               pos = Math.max(A._min, splitter._DA - B._max, Math.min(pos, A._max, splitter._DA - bar._DA - B._min));
            }
            splitter._leadingPane._setNewSizeFromPos(pos);
            triggerUpdated();
         }
         function resplit(pos) {
            bar._DA = bar[0][opts.pxSplit]; 	// bar size may change during dock
            // Constrain new splitbar position to fit pane size and docking limits
            if ((opts.dockPane === A && pos < Math.max(A._min, bar._DA)) ||
               (opts.dockPane === B && pos > Math.min(pos, A._max, splitter._DA - bar._DA - B._min))) {
               bar.addClass(opts.barDockedClass);
               bar._DA = bar[0][opts.pxSplit];
               pos = opts.dockPane === A ? 0 : splitter._DA - bar._DA;
            }
            else {
               bar.removeClass(opts.barDockedClass);
               bar._DA = bar[0][opts.pxSplit];
               pos = Math.max(A._min, splitter._DA - B._max,
                  Math.min(pos, A._max, splitter._DA - bar._DA - B._min));
            }
            // Resize/position the two panes
            bar.css(opts.origin, pos).css(opts.fixed, splitter._DF);
            A.css(opts.origin, 0).css(opts.split, pos).css(opts.fixed, splitter._DF);
            B.css(opts.origin, pos + bar._DA)
               .css(opts.split, splitter._DA - bar._DA - pos).css(opts.fixed, splitter._DF);
            panes.trigger("resize");
         }

         function getDockPos() {
            return opts.dockPane === A ? 0 : splitter[0][opts.pxSplit] - splitter._PBA - bar[0][opts.pxSplit];
         }
         function dimSum(jq, dims) {
            // Opera returns -1 for missing min/max width, turn into 0
            var sum = 0;
            for (var i = 1; i < arguments.length; i++)
               sum += Math.max(parseInt(jq.css(arguments[i]), 10) || 0, 0);
            return sum;
         }

         // Determine settings based on incoming opts, element classes, and defaults
         var vh = (args.splitHorizontal ? 'h' : args.splitVertical ? 'v' : args.type) || 'v';
         var opts = $.extend({
            // Defaults here allow easy use with ThemeRoller
            splitterClass: "splitter ui-widget ui-widget-content",
            paneClass: "splitter-pane",
            barClass: "splitter-bar",
            barNormalClass: "ui-state-default", 		// splitbar normal
            barHoverClass: "ui-state-hover", 		// splitbar mouse hover
            barActiveClass: "ui-state-highlight", 	// splitbar being moved
            barLimitClass: "ui-state-error", 		// splitbar at limit
            iframeClass: "splitter-iframe-hide", 	// hide iframes during split
            eventNamespace: ".splitter" + (++splitterCounter),
            pxPerKey: 8, 		   // splitter px moved per keypress
            tabIndex: 0, 		   // tab order indicator
            accessKey: '',			// accessKey for splitbar
            zIndexBar: 10,       //z-index for the splitbar
            zIndexPanes: 1       //z-index for the splitted panes
         }, {
            // user can override
            v: {					// Vertical splitters:
               keyLeft: 39, keyRight: 37, cursor: "e-resize",
               barStateClass: "splitter-bar-vertical",
               barDockedClass: "splitter-bar-vertical-docked"
            },
            h: {					// Horizontal splitters:
               keyTop: 40, keyBottom: 38, cursor: "n-resize",
               barStateClass: "splitter-bar-horizontal",
               barDockedClass: "splitter-bar-horizontal-docked"
            }
         }[vh], args, {
            // user cannot override
            v: {					// Vertical splitters:
               type: 'v', eventPos: "pageX", origin: "left",
               split: "width", pxSplit: "offsetWidth", side1: "Left", side2: "Right",
               fixed: "height", pxFixed: "offsetHeight", side3: "Top", side4: "Bottom"
            },
            h: {					// Horizontal splitters:
               type: 'h', eventPos: "pageY", origin: "top",
               split: "height", pxSplit: "offsetHeight", side1: "Top", side2: "Bottom",
               fixed: "width", pxFixed: "offsetWidth", side3: "Left", side4: "Right"
            }
         }[vh]);
         opts.barStateClasses = [opts.barNormalClass, opts.barHoverClass, opts.barActiveClass, opts.barLimitClass].join(' ');

         // Create jQuery object closures for splitter and both panes
         var splitter = $(this).css({ position: "absolute" }).addClass(opts.splitterClass);
         var panes = $(">*", splitter[0]).addClass(opts.paneClass).css({
            position: "absolute", 			// positioned inside splitter container
            "z-index": opts.zIndexPanes,  // splitbar is positioned above
            "-moz-outline-style": "none"	// don't show dotted outline
         });
         var A = $(panes[0]), B = $(panes[1]); // A = left/top, B = right/bottom
         opts.dockPane = opts.dock && (/right|bottom/.test(opts.dock) ? B : A);

         // Focuser element, provides keyboard support; title is shown by Opera accessKeys
         var focuser = $('<a href="javascript:void(0)"></a>')
            .attr({ accessKey: opts.accessKey, tabIndex: opts.tabIndex, title: opts.splitbarClass })
            .bind("focus" + opts.eventNamespace, function () {
               this.focus(); bar.addClass(opts.barActiveClass)
            })
            .bind("keydown" + opts.eventNamespace, function (e) {
               var key = e.which || e.keyCode;
               var dir = key === opts["key" + opts.side1] ? 1 : key === opts["key" + opts.side2] ? -1 : 0;
               if (dir) {
                  var pos = A[0][opts.pxSplit] + dir * opts.pxPerKey;
                  resplit(pos);
                  adjustPaneAdmin(pos);
               }
            })
            .bind("blur" + opts.eventNamespace, function () { bar.removeClass(opts.barActiveClass) });

         // Splitbar element
         var bar = $('<div></div>')
            .insertAfter(A).addClass(opts.barClass).addClass(opts.barStateClass)
            .append(focuser).attr({ unselectable: "on" })
            .css({
               position: "absolute", "user-select": "none", "-webkit-user-select": "none",
               "-khtml-user-select": "none", "-moz-user-select": "none", "z-index": opts.zIndexBar
            })
            .bind("mousedown" + opts.eventNamespace, startSplitMouse)
            .bind("mouseover" + opts.eventNamespace, function () {
               $(this).addClass(opts.barHoverClass);
            })
            .bind("mouseout" + opts.eventNamespace, function () {
               $(this).removeClass(opts.barHoverClass);
            });
         // Use our cursor unless the style specifies a non-default cursor
         if (/^(auto|default|)$/.test(bar.css("cursor")))
            bar.css("cursor", opts.cursor);

         // Cache several dimensions for speed, rather than re-querying constantly
         // These are saved on the A/B/bar/splitter jQuery vars, which are themselves cached
         // DA=dimension adjustable direction, PBF=padding/border fixed, PBA=padding/border adjustable
         bar._DA = bar[0][opts.pxSplit];
         splitter._PBF = dimSum(splitter, "border" + opts.side3 + "Width", "border" + opts.side4 + "Width");
         splitter._PBA = dimSum(splitter, "border" + opts.side1 + "Width", "border" + opts.side2 + "Width");
         splitter._clientSize = function () {
            return (this[0])[opts.pxSplit] - splitter._PBA - bar._DA;
         }
         A._pane = opts.side1;
         A._index = 0;
         B._pane = opts.side2;
         B._index = 1;

         var leadingPane;
         $.each([A, B], function () {
            this._splitter_style = this.style;
            this._min = opts["min" + this._pane] || dimSum(this, "min-" + opts.split);
            this._max = opts["max" + this._pane] || dimSum(this, "max-" + opts.split) || 9999;
            var pos = opts["size" + this._pane] === true ? parseInt($.curCSS(this[0], opts.split), 10)
                                                         : opts["size" + this._pane];
            this._factor = 0;
            if (isNaN(pos)) return;
            leadingPane = this;
            this._init = pos;
            if (pos > 0 && pos < 1) this._factor = pos;
            if (opts.debug) console.log("PANE: ", this._pane, ", init=", this._init, ", fact=", this._factor);
         });

         //Make A leading if non of the panels is leading
         if (!leadingPane) {
            leadingPane = A;
            A._factor = .5;
            A._init = Math.round(splitter._clientSize / 2);
         }
         splitter._leadingPane = leadingPane;

         leadingPane._getSplitPos = function (useInitial) {
            var pos = this._init;
            if (isNaN(pos)) return pos;
            if (!useInitial) pos = (this[0])[opts.pxSplit];

            var clientSize = splitter._clientSize();
            if (this._factor !== 0) pos = clientSize * this._factor;
            if (this._index === 1) pos = clientSize - pos;
            return pos;
         };
         leadingPane._setNewSize = function (size) {
            if (size === 0) console.trace('size is 0');
            if (opts.debug) console.log('_setNewSize', size, this[0].id, 'f', this._factor, 'i', this._init, 'ix', this._index, 'docked', bar._docked);
            var clientSize = splitter._clientSize();
            if (size > 0 && size < 1) {
               this._factor = size;
               size *= clientSize;
            } else {
               if (this._factor !== 0) this._factor = size / clientSize;
            }
            this._init = size;
            if (opts.debug) console.log('-- f', this._factor, 'init', this._init, 'splitpos', this._index === 0 ? size : clientSize - size);
            return this._index === 0 ? size : clientSize - size;
         };
         leadingPane._setNewSizeFromPos = leadingPane._index === 0
            ? leadingPane._setNewSize
            : function (pos) { return this._setNewSize(splitter._clientSize() - pos); };


         // Resize event propagation and splitter sizing
         if (opts.anchorToWindow)
            opts.resizeTo = window;
         if (opts.resizeTo) {
            splitter._hadjust = dimSum(splitter, "borderTopWidth", "borderBottomWidth", "marginBottom");
            splitter._hmin = Math.max(dimSum(splitter, "minHeight"), 20);
            $(window).bind("resize" + opts.eventNamespace, function (ev) {
               if (ev.target !== window) return;
               var top = splitter.offset().top;
               var eh = $(opts.resizeTo).height();
               splitter.css("height", Math.max(eh - top - splitter._hadjust, splitter._hmin) + "px");
               splitter.trigger("resize");
            }).trigger("resize" + opts.eventNamespace);
         }
         else if (opts.resizeToWidth) {
            $(window).bind("resize" + opts.eventNamespace, function (ev) {
               if (ev.target !== window) return;
               splitter.triggerHandler("resize");
            });
         }

         splitter.bind("getState" + opts.eventNamespace, function () {
            var leading = splitter._leadingPane;
            if (opts.debug) console.log('GETSTATE leading', leading._index, 'f', leading._factor); 
            return {
               pos: leading._factor === 0 ? leading._init : leading._factor,
               docked: bar._docked ? true : false,
               lastDockWasManual: bar._lastDockWasManual
            };
         }).bind("setState" + opts.eventNamespace, function (e, state, animate) {
            var leading = splitter._leadingPane;
            if (opts.debug) console.log('SETSTATE leading', leading._index, 'state', state);

            var splitPos;

            if (state.pos !== undefined) {
               if (opts.debug) console.log('-- resplit(defer)');
               splitPos = leading._setNewSize(state.pos);
            }
            if (state.docked !== undefined) {
               var curDocked = bar._docked ? true : false;
               var newDocked = state.docked ? true : false;
               if (opts.debug) console.log('-- dock cur', curDocked, 'new', newDocked);
               if (curDocked !== newDocked) {
                  if (splitter.triggerHandler(newDocked ? "dock" : "undock", [animate]))
                     splitPos = undefined;
               }
            }
            if (splitPos !== undefined) {
               if (opts.debug) console.log('-- resplit(', splitPos, ')');
               resplit(splitPos);
            }
            if (state.lastDockWasManual !== undefined) {
               bar._lastDockWasManual = state.lastDockWasManual ? true : false;
            }
         });


         // Docking support
         splitter.bind("toggleDock" + opts.eventNamespace, function () {
            splitter.triggerHandler(bar._docked ? "undock" : "dock");
         }).bind("dock" + opts.eventNamespace, function (e, animate) {
            if (opts.debug) console.log('DOCK cur=', bar._docked);
            if (bar._docked) return false;

            var leading = splitter._leadingPane;
            var x = {};
            x[opts.origin] = getDockPos(); //opts.dockPane === A ? 0 : splitter[0][opts.pxSplit] - splitter._PBA - bar[0][opts.pxSplit];

            bar._docked = true;
            bar._lastDockWasManual = false;
            bar.addClass(opts.barDockedClass);
            if (animate !== false)
               bar.animate(x, opts.dockSpeed || 1, opts.dockEasing, function () { resplit(x[opts.origin]); });
            else
               resplit(x[opts.origin]);

            if (opts.debug) console.log('-- cur=', bar._docked, 'leading', leading);
            triggerUpdated();
            return true;
         }).bind("undock" + opts.eventNamespace, function (e, animate) { 
            if (opts.debug) console.log('UNDOCK cur=', bar._docked);
            if (!bar._docked) return false;

            var leading = splitter._leadingPane;
            if (opts.debug) console.log('--leading', leading);
            bar._docked = false;
            bar._lastDockWasManual = false;
            bar.removeClass(opts.barDockedClass);
            var pos = leading._getSplitPos(true);
            if (animate !== false) {
               var x = {}; x[opts.origin] = pos + "px";
               bar.animate(x, opts.undockSpeed || opts.dockSpeed || 1, opts.undockEasing || opts.dockEasing,
                  function () { resplit(pos); });
            } else 
               resplit(pos);

            if (opts.debug) console.log('-- cur=', bar._docked, 'leading', leading);
            triggerUpdated();
            return true;
         });

         if (opts.dock) {
            if (opts.dockKey)
               $('<a title="' + opts.splitbarClass + ' toggle dock" href="javascript:void(0)"></a>')
               .attr({ accessKey: opts.dockKey, tabIndex: -1 }).appendTo(bar)
               .bind("focus", function () {
                  splitter.triggerHandler("toggleDock"); this.blur();
                  bar._dockOperationInitiated = true;
                  bar._lastDockWasManual = true; //Manual intervention detected
               });
            bar.bind("dblclick", function () {
               splitter.triggerHandler("toggleDock");
               bar._lastDockWasManual = true; //Manual intervention detected
            })
         }

         // Resize event handler; triggered immediately to set initial position
         splitter.bind("destroy" + opts.eventNamespace, function () {
            $([window, document]).unbind(opts.eventNamespace);
            bar.unbind().remove();
            panes.removeClass(opts.paneClass);
            splitter
               .removeClass(opts.splitterClass)
               .add(panes)
                  .unbind(opts.eventNamespace)
                  .attr("style", function (el) {
                     return this._splitter_style || ""; //TODO: save style
                  });
            splitter = bar = focuser = panes = A = B = opts = args = null;
         }).bind("resize" + opts.eventNamespace, function (e, size) {
            // Custom events bubble in jQuery 1.3; avoid recursion
            if (e.target !== this) return;
            // Determine new width/height of splitter container
            splitter._DF = splitter[0][opts.pxFixed] - splitter._PBF;
            splitter._DA = splitter[0][opts.pxSplit] - splitter._PBA;
            // Bail if splitter isn't visible or content isn't there yet
            if (splitter._DF <= 0 || splitter._DA <= 0) return;
            // Re-divvy the adjustable dimension; maintain size of the preferred pane

            var splitPos = size;
            if (isNaN(size)) {
               splitPos = bar._docked ? getDockPos() : splitter._leadingPane._getSplitPos();
            }
            resplit(splitPos);
            setBarState(opts.barNormalClass);
         }).trigger("resize", [splitter._leadingPane._getSplitPos(true)]);
      });
   };

})(jQuery);