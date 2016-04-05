var MyAndromeda;
(function (MyAndromeda) {
    var WebOrdering;
    (function (WebOrdering) {
        "use strict";
        var Logger = (function () {
            function Logger() {
                this.UseNotify = true;
                this.UseDebug = true;
                this.UseError = true;
            }
            Logger.Notify = function (o) {
                if (logger.UseNotify) {
                    console.log(o);
                }
            };
            Logger.Debug = function (o) {
                if (logger.UseDebug) {
                    console.log(o);
                }
            };
            Logger.Error = function (o) {
                if (logger.UseError) {
                    console.log(o);
                }
            };
            Logger.SettingUpController = function (name, state) {
                if (logger.UseNotify) {
                    console.log("setting up controller - " + name + " : " + state);
                }
            };
            Logger.SettingUpService = function (name, state) {
                if (logger.UseNotify) {
                    console.log("setting up service - " + name + " : " + state);
                }
            };
            Logger.AllowDebug = function (value) {
                logger.UseDebug = value;
            };
            Logger.AllowError = function (value) {
                logger.UseError = value;
            };
            return Logger;
        }());
        WebOrdering.Logger = Logger;
        var logger = new Logger();
    })(WebOrdering = MyAndromeda.WebOrdering || (MyAndromeda.WebOrdering = {}));
})(MyAndromeda || (MyAndromeda = {}));
